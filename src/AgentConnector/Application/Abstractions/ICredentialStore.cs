namespace AgentConnector;

public interface ICredentialStore
{
    ValueTask<ConnectorCredential?> GetAsync(ConnectionKey key, CancellationToken cancellationToken = default);
    ValueTask SetAsync(ConnectionKey key, ConnectorCredential credential, CancellationToken cancellationToken = default);
    ValueTask DeleteAsync(ConnectionKey key, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ConnectionInfo> ListAsync(string tenantId, CancellationToken cancellationToken = default);
}