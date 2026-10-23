using System.Net;

namespace AgentConnector;

public sealed class ConnectorConnectionService(ProviderCatalog catalog, ICredentialStore credentials,
    ConnectionCoordinator coordinator, ConnectorOAuth oauth)
{
    public async Task<ConnectionInfo> SetAsync(ConnectionKey key, ConnectorCredential credential,
        CancellationToken cancellationToken = default)
    {
        key.Validate();
        var provider = catalog.GetProvider(key.ProviderId);
        if (provider.CredentialKind == CredentialKind.None)
            throw new ConnectorException("invalid_input", "This provider does not use credentials.");
        credential.Validate(provider);
        using var lease = await coordinator.LockAsync(key, cancellationToken);
        coordinator.Invalidate(key);
        await credentials.SetAsync(key, credential, cancellationToken);
        return new(key.ProviderId, key.Name, credential.Kind, credential.ExpiresAt);
    }

    public async Task DeleteAsync(ConnectionKey key, CancellationToken cancellationToken = default)
    {
        key.Validate();
        catalog.GetProvider(key.ProviderId);
        using var lease = await coordinator.LockAsync(key, cancellationToken);
        coordinator.Invalidate(key);
        await credentials.DeleteAsync(key, cancellationToken);
    }

    public IAsyncEnumerable<ConnectionInfo> ListAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ConnectionKey.ValidateIdentity(tenantId);
        return credentials.ListAsync(tenantId, cancellationToken);
    }

    internal async Task<ConnectorCredential?> GetCredentialAsync(ConnectionKey key, ConnectorProvider provider,
        IReadOnlyList<string>? requiredScopes, CancellationToken cancellationToken)
    {
        if (provider.CredentialKind == CredentialKind.None)
            return null;

        using var lease = await coordinator.LockAsync(key, cancellationToken);
        var credential = await credentials.GetAsync(key, cancellationToken)
            ?? throw new ConnectorException("connection_not_found", "The selected connection does not exist.", HttpStatusCode.NotFound);
        if (credential.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(60))
        {
            credential = await oauth.RefreshAsync(provider, credential, cancellationToken);
            await credentials.SetAsync(key, credential, cancellationToken);
        }
        if (requiredScopes is { Count: > 0 } && credential.Scopes is { } granted && requiredScopes.Except(granted, StringComparer.Ordinal).Any())
            throw new ConnectorException("scope_missing", "The connection does not have the required scopes.", HttpStatusCode.Forbidden);

        return credential;
    }
}