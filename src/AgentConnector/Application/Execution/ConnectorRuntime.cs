using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace AgentConnector;

public sealed class ConnectorRuntime(ConnectorOptions options, ProviderCatalog catalog, ConnectorPolicy policy,
    ConnectorConnectionService connections,
    ConnectorTransport transport, IConnectorAuditSink audit, ILogger<ConnectorRuntime> logger)
{
    public ConnectorRuntime(ConnectorOptions options, ProviderCatalog catalog, ConnectorPolicy policy,
        ICredentialStore credentials, ConnectionCoordinator coordinator, ConnectorOAuth oauth,
        ConnectorTransport transport, IConnectorAuditSink audit, ILogger<ConnectorRuntime> logger)
        : this(options, catalog, policy, new ConnectorConnectionService(catalog, credentials, coordinator, oauth),
            transport, audit, logger)
    {
    }

    public async Task<ActionResult> ExecuteAsync(string tenantId, string actionId, JsonObject input,
        string connectionName = "default", CancellationToken cancellationToken = default)
    {
        var (provider, action) = catalog.GetAction(actionId);
        policy.DemandAction(actionId);
        DemandConfigured(provider);
        var key = new ConnectionKey(tenantId, provider.Id, connectionName);
        key.Validate();
        DemandInputSize(input);
        if (provider.Id is "openai" or "anthropic" or "cohere" or "mistral" or "groq" &&
            input["body"] is JsonObject body && body["stream"] is JsonValue stream && stream.TryGetValue<bool>(out var streaming) && streaming)
            throw new ConnectorException("unsupported_feature", "Streaming responses are not supported by this JSON runtime.");
        using var request = ProviderRequestBuilder.Build(provider, action, input);
        return await ExecuteRequestAsync(key, actionId, provider, request, action.RequiredScopes, cancellationToken);
    }

    public async Task<ActionResult> ProxyAsync(string tenantId, string providerId, ProxyRequest input,
        string connectionName = "default", CancellationToken cancellationToken = default)
    {
        var provider = catalog.GetProvider(providerId);
        policy.DemandProxy(providerId);
        DemandConfigured(provider);
        var key = new ConnectionKey(tenantId, providerId, connectionName);
        key.Validate();
        DemandInputSize(input.Body);
        DemandInputSize(input.Query);
        using var request = ProviderRequestBuilder.BuildProxy(provider, input);
        return await ExecuteRequestAsync(key, providerId + ".proxy", provider, request, null, cancellationToken);
    }

    public Task<ConnectionInfo> SetConnectionAsync(ConnectionKey key, ConnectorCredential credential, CancellationToken cancellationToken = default) =>
        connections.SetAsync(key, credential, cancellationToken);

    public Task DeleteConnectionAsync(ConnectionKey key, CancellationToken cancellationToken = default) =>
        connections.DeleteAsync(key, cancellationToken);

    public IAsyncEnumerable<ConnectionInfo> ListConnectionsAsync(string tenantId, CancellationToken cancellationToken = default) =>
        connections.ListAsync(tenantId, cancellationToken);

    private async Task<ActionResult> ExecuteRequestAsync(ConnectionKey key, string actionId, ConnectorProvider provider,
        HttpRequestMessage request, IReadOnlyList<string>? requiredScopes, CancellationToken cancellationToken)
    {
        var executionId = Guid.NewGuid().ToString("N");
        var started = DateTimeOffset.UtcNow;
        var timer = Stopwatch.StartNew();
        var success = false;
        string? errorCode = null;
        try
        {
            var credential = await connections.GetCredentialAsync(key, provider, requiredScopes, cancellationToken);
            ProviderRequestBuilder.Authenticate(request, provider, credential);
            var response = await transport.SendAsync(request, cancellationToken);
            DemandProviderSuccess(provider, response.Data);
            success = true;
            return new(response.Data, response.Status, executionId);
        }
        catch (ConnectorException exception) { errorCode = exception.Code; throw; }
        catch (OperationCanceledException) { errorCode = "execution_cancelled"; throw; }
        catch { errorCode = "execution_failed"; throw; }
        finally
        {
            try
            {
                await audit.WriteAsync(new(executionId, key.TenantId, provider.Id, actionId, key.Name, started, timer.Elapsed, success, errorCode), CancellationToken.None);
            }
            catch { logger.LogError("Connector audit sink failed for execution {ExecutionId}.", executionId); }
        }
    }

    private void DemandConfigured(ConnectorProvider provider)
    {
        if (provider.RequiresBaseUri && !options.ProviderBaseUris.ContainsKey(provider.Id))
            throw new ConnectorException("provider_not_configured", "This provider requires a trusted instance base URI in server configuration.");
    }

    private void DemandInputSize(JsonNode? input)
    {
        if (input is not null && Encoding.UTF8.GetByteCount(input.ToJsonString()) > options.MaximumRequestBytes)
            throw new ConnectorException("request_too_large", "Input exceeds the configured request size limit.", HttpStatusCode.RequestEntityTooLarge);
    }

    private static void DemandProviderSuccess(ConnectorProvider provider, JsonNode? data)
    {
        if ((provider.Id == "slack" && data is JsonObject && data["ok"]?.ToString() == "false") ||
            (provider.Id == "cloudflare" && data is JsonObject && data["success"]?.ToString() == "false") ||
            (provider.Id is "linear" or "monday" or "shopify" && data is JsonObject && data["errors"] is JsonArray { Count: > 0 }))
            throw new ConnectorException("provider_error", "The provider reported an application-level error.", HttpStatusCode.BadGateway);
    }
}