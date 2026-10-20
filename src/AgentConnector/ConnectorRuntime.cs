using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace AgentConnector;

public sealed class ConnectorRuntime(ConnectorOptions options, ProviderCatalog catalog, ConnectorPolicy policy,
    ICredentialStore credentials, ConnectionCoordinator coordinator, ConnectorOAuth oauth,
    ConnectorTransport transport, IConnectorAuditSink audit, ILogger<ConnectorRuntime> logger)
{
    public async Task<ActionResult> ExecuteAsync(string tenantId, string actionId, JsonObject input,
        string connectionName = "default", CancellationToken cancellationToken = default)
    {
        var (provider, action) = catalog.GetAction(actionId);
        policy.DemandAction(actionId);
        DemandConfigured(provider);
        var key = new ConnectionKey(tenantId, provider.Id, connectionName);
        EncryptedFileCredentialStore.ValidateKey(key);
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
        EncryptedFileCredentialStore.ValidateKey(key);
        DemandInputSize(input.Body);
        DemandInputSize(input.Query);
        using var request = ProviderRequestBuilder.BuildProxy(provider, input);
        return await ExecuteRequestAsync(key, providerId + ".proxy", provider, request, null, cancellationToken);
    }

    public async Task<ConnectionInfo> SetConnectionAsync(ConnectionKey key, ConnectorCredential credential, CancellationToken cancellationToken = default)
    {
        EncryptedFileCredentialStore.ValidateKey(key);
        var provider = catalog.GetProvider(key.ProviderId);
        if (provider.CredentialKind == CredentialKind.None) throw new ConnectorException("invalid_input", "This provider does not use credentials.");
        ProviderRequestBuilder.ValidateCredential(provider, credential);
        using var lease = await coordinator.LockAsync(key, cancellationToken);
        coordinator.Invalidate(key);
        await credentials.SetAsync(key, credential, cancellationToken);
        return new(key.ProviderId, key.Name, credential.Kind, credential.ExpiresAt);
    }

    public async Task DeleteConnectionAsync(ConnectionKey key, CancellationToken cancellationToken = default)
    {
        EncryptedFileCredentialStore.ValidateKey(key);
        catalog.GetProvider(key.ProviderId);
        using var lease = await coordinator.LockAsync(key, cancellationToken);
        coordinator.Invalidate(key);
        await credentials.DeleteAsync(key, cancellationToken);
    }

    public IAsyncEnumerable<ConnectionInfo> ListConnectionsAsync(string tenantId, CancellationToken cancellationToken = default) =>
        credentials.ListAsync(tenantId, cancellationToken);

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
            ConnectorCredential? credential = null;
            if (provider.CredentialKind != CredentialKind.None)
            {
                using var lease = await coordinator.LockAsync(key, cancellationToken);
                credential = await credentials.GetAsync(key, cancellationToken)
                    ?? throw new ConnectorException("connection_not_found", "The selected connection does not exist.", HttpStatusCode.NotFound);
                if (credential.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(60))
                {
                    credential = await oauth.RefreshAsync(provider, credential, cancellationToken);
                    await credentials.SetAsync(key, credential, cancellationToken);
                }
                if (requiredScopes is { Count: > 0 } && credential.Scopes is { } granted && requiredScopes.Except(granted, StringComparer.Ordinal).Any())
                    throw new ConnectorException("scope_missing", "The connection does not have the required scopes.", HttpStatusCode.Forbidden);
            }
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

public sealed class LoggingConnectorAuditSink(ILogger<LoggingConnectorAuditSink> logger) : IConnectorAuditSink
{
    public ValueTask WriteAsync(ExecutionAudit audit, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Connector execution {ExecutionId} action {ActionId} success {Success} error {ErrorCode} duration {DurationMs}ms",
            audit.ExecutionId, audit.ActionId, audit.Success, audit.ErrorCode, audit.Duration.TotalMilliseconds);
        return ValueTask.CompletedTask;
    }
}