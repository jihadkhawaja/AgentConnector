using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentConnector;

public static class EndpointRouteBuilderExtensions
{
    private static readonly JsonSerializerOptions InputOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 32,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter<CredentialKind>(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) }
    };

    public static IEndpointRouteBuilder MapAgentConnector(this IEndpointRouteBuilder endpoints, string prefix = "/connector")
    {
        var options = endpoints.ServiceProvider.GetRequiredService<ConnectorOptions>();
        var root = endpoints.MapGroup(prefix);
        root.AddEndpointFilter(async (invocation, next) =>
        {
            var context = invocation.HttpContext;
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            try { return await next(invocation); }
            catch (ConnectorException exception)
            {
                if (exception.RetryAfter is { } retry)
                    context.Response.Headers.RetryAfter = Math.Ceiling(retry.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                return Error(exception.Code, exception.Message, (int)exception.Status);
            }
            catch (JsonException) { return Error("invalid_input", "Request JSON does not match the expected schema.", 400); }
            catch (CryptographicException) { return Error("credential_store_unavailable", "Credential protection could not be accessed.", 503); }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
            catch
            {
                context.RequestServices.GetRequiredService<ILogger<ConnectorRuntime>>().LogError("Connector request {RequestId} failed.", context.TraceIdentifier);
                return Error("internal_error", "The connector request could not be completed.", 500);
            }
        });

        var runtime = root.MapGroup("/v1").RequireAuthorization(options.RuntimePolicyName);
        runtime.MapGet("/health", (HttpContext context) => { Tenant(context, options); return Success(new { status = "ok" }); });
        runtime.MapGet("/providers", (HttpContext context, ProviderCatalog catalog) =>
        {
            Tenant(context, options);
            return Success(catalog.Providers.OrderBy(provider => provider.Id).Select(provider => new
            {
                provider.Id, provider.DisplayName, authType = provider.CredentialKind,
                oauthSupported = provider.OAuth is not null || options.OAuthClients.ContainsKey(provider.Id),
                configured = !provider.RequiresBaseUri || options.ProviderBaseUris.ContainsKey(provider.Id), actionCount = provider.Actions.Count
            }));
        });
        runtime.MapGet("/actions", (HttpContext context, ProviderCatalog catalog, ConnectorPolicy policy) =>
        {
            Tenant(context, options);
            var service = context.Request.Query["service"].ToString();
            var search = context.Request.Query["q"].ToString();
            return Success(catalog.Actions.Where(action => policy.IsActionAllowed(action.Id) &&
                (service.Length == 0 || action.Id.StartsWith(service + ".", StringComparison.Ordinal)) &&
                (search.Length == 0 || action.Id.Contains(search, StringComparison.OrdinalIgnoreCase))).Select(Describe));
        });
        runtime.MapGet("/actions/{actionId}", (HttpContext context, string actionId, ProviderCatalog catalog, ConnectorPolicy policy) =>
        {
            Tenant(context, options);
            var (_, action) = catalog.GetAction(actionId);
            policy.DemandAction(actionId);
            return Success(Describe(action));
        });
        runtime.MapPost("/actions/{actionId}", async (HttpContext context, string actionId, ConnectorRuntime connector) =>
        {
            var tenantId = Tenant(context, options);
            if (context.Request.Headers.ContainsKey("Idempotency-Key"))
                throw new ConnectorException("unsupported_feature", "Durable idempotency is not implemented; this header cannot guarantee duplicate suppression.");
            var body = await ReadAsync<ActionExecutionRequest>(context, options);
            if (body.Input is null) throw new ConnectorException("invalid_input", "The input object is required.");
            var result = await connector.ExecuteAsync(tenantId, actionId, body.Input, ConnectionName(context, body.ConnectionName), context.RequestAborted);
            return Execution(result, actionId);
        });
        runtime.MapPost("/proxy/{providerId}", async (HttpContext context, string providerId, ConnectorRuntime connector) =>
        {
            var tenantId = Tenant(context, options);
            var body = await ReadAsync<ProxyExecutionRequest>(context, options);
            if (string.IsNullOrEmpty(body.Endpoint)) throw new ConnectorException("invalid_input", "The endpoint is required.");
            var result = await connector.ProxyAsync(tenantId, providerId, new(body.Endpoint, body.Method, body.Query, body.Body),
                ConnectionName(context, body.ConnectionName), context.RequestAborted);
            return Success(new { status = result.Status, data = result.Data }, new { result.ExecutionId });
        });
        runtime.MapGet("/connections", async (HttpContext context, ConnectorRuntime connector) =>
            Success(await Connections(connector, Tenant(context, options), context.RequestAborted)));

        var admin = root.MapGroup("/api").RequireAuthorization(options.AdminPolicyName);
        admin.MapGet("/connections", async (HttpContext context, ConnectorRuntime connector) =>
            Success(await Connections(connector, Tenant(context, options), context.RequestAborted)));
        admin.MapPut("/connections/{providerId}", async (HttpContext context, string providerId, ConnectorRuntime connector) =>
        {
            var tenantId = Tenant(context, options);
            var body = await ReadAsync<SaveConnectionRequest>(context, options);
            if (body.Credential is null) throw new ConnectorException("invalid_input", "The credential object is required.");
            return Success(await connector.SetConnectionAsync(new(tenantId, providerId, ConnectionName(context, body.ConnectionName)), body.Credential, context.RequestAborted));
        });
        admin.MapDelete("/connections/{providerId}", async (HttpContext context, string providerId, ConnectorRuntime connector) =>
        {
            await connector.DeleteConnectionAsync(new(Tenant(context, options), providerId, ConnectionName(context, null)), context.RequestAborted);
            return Success(new { deleted = true });
        });
        admin.MapPost("/oauth/{providerId}/authorize", async (HttpContext context, string providerId, ConnectorOAuth oauth) =>
        {
            var tenantId = Tenant(context, options);
            var body = await ReadAsync<BeginOAuthRequest>(context, options);
            return Success(await oauth.BeginAsync(new(tenantId, providerId, ConnectionName(context, body.ConnectionName)), context.RequestAborted));
        });
        root.MapGet("/oauth/callback", async (HttpContext context, ConnectorOAuth oauth) =>
            Success(await oauth.CompleteAsync(context.Request.Query["state"].ToString(), context.Request.Query["code"].ToString(),
                context.Request.Query.ContainsKey("error") ? "denied" : null, context.RequestAborted))).AllowAnonymous();
        root.MapGet("/openapi.json", (HttpContext context, ProviderCatalog catalog, ConnectorPolicy policy) =>
        {
            Tenant(context, options);
            return Results.Json(ActionSchemas.OpenApi(catalog, policy, context.Request.PathBase + prefix));
        }).RequireAuthorization(options.RuntimePolicyName);
        return endpoints;
    }

    private static object Describe(ConnectorAction action) => new
    {
        action.Id, action.Description, operationType = action.IsReadOnly ? "read" : "write",
        inputSchema = ActionSchemas.Input(action), requiredScopes = action.RequiredScopes ?? [], locallyExecutable = true
    };

    private static string Tenant(HttpContext context, ConnectorOptions options)
    {
        var tenants = context.User.FindAll(options.TenantClaimType).Select(claim => claim.Value).Distinct(StringComparer.Ordinal).ToArray();
        if (tenants.Length != 1 || string.IsNullOrWhiteSpace(tenants[0]))
            throw new ConnectorException("tenant_required", "Exactly one authenticated tenant identity is required.", HttpStatusCode.Forbidden);
        return tenants[0];
    }

    private static string ConnectionName(HttpContext context, string? bodyName)
    {
        if (context.Request.Headers.ContainsKey("x-oo-connector-app-id"))
            throw new ConnectorException("unsupported_selector", "Stable connection IDs are not supported. Select an explicit connection name.");
        var selectors = context.Request.Query["alias"].Concat(context.Request.Query["connectionName"])
            .Concat(context.Request.Headers["x-oo-connector-alias"]).Concat(bodyName is null ? [] : new[] { bodyName }).ToArray();
        if (selectors.Any(string.IsNullOrWhiteSpace) || selectors.Distinct(StringComparer.Ordinal).Count() > 1)
            throw new ConnectorException("invalid_input", "Connection selectors are empty or conflict.");
        return selectors.FirstOrDefault() ?? "default";
    }

    private static async Task<T> ReadAsync<T>(HttpContext context, ConnectorOptions options)
    {
        if (!context.Request.HasJsonContentType()) throw new ConnectorException("invalid_input", "Content-Type must be application/json.", HttpStatusCode.UnsupportedMediaType);
        if (context.Request.ContentLength > options.MaximumRequestBytes) throw Oversized();
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await context.Request.Body.ReadAsync(chunk, context.RequestAborted)) > 0)
        {
            if (buffer.Length + count > options.MaximumRequestBytes) throw Oversized();
            buffer.Write(chunk, 0, count);
        }
        return JsonSerializer.Deserialize<T>(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), InputOptions)
            ?? throw new ConnectorException("invalid_input", "A JSON object is required.");
    }

    private static async Task<List<ConnectionInfo>> Connections(ConnectorRuntime connector, string tenantId, CancellationToken cancellationToken)
    {
        var results = new List<ConnectionInfo>();
        await foreach (var connection in connector.ListConnectionsAsync(tenantId, cancellationToken)) results.Add(connection);
        return results;
    }

    private static IResult Success(object? data, object? meta = null) => Results.Json(new { success = true, message = "OK", data, meta }, InputOptions);
    private static IResult Execution(ActionResult result, string actionId) => Success(result.Data, new { result.ExecutionId, actionId, providerStatus = result.Status });
    private static IResult Error(string code, string message, int status) => Results.Json(new { success = false, errorCode = code, message }, statusCode: status);
    private static ConnectorException Oversized() => new("request_too_large", "Request exceeds the configured size limit.", HttpStatusCode.RequestEntityTooLarge);

    private sealed record ActionExecutionRequest(JsonObject? Input, string? ConnectionName = null);
    private sealed record ProxyExecutionRequest(string Endpoint, string Method = "GET", JsonObject? Query = null, JsonNode? Body = null, string? ConnectionName = null);
    private sealed record SaveConnectionRequest(ConnectorCredential? Credential, string? ConnectionName = null);
    private sealed record BeginOAuthRequest(string? ConnectionName = null);
}