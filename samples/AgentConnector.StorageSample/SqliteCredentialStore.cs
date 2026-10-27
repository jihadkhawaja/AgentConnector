using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;

namespace AgentConnector.StorageSample;

/// <summary>Sample database store. Credential payloads, including metadata, use identity-bound Data Protection.</summary>
public sealed class SqliteCredentialStore(string databasePath, IDataProtectionProvider protection) : ICredentialStore
{
    private readonly string connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = Path.GetFullPath(databasePath),
        Mode = SqliteOpenMode.ReadWriteCreate,
        Pooling = false,
        DefaultTimeout = 5
    }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS connector_credentials (
                tenant_id TEXT NOT NULL,
                connection_id TEXT NOT NULL,
                payload BLOB NOT NULL CHECK(length(payload) <= 1048576),
                PRIMARY KEY (tenant_id, connection_id)
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<ConnectorCredential?> GetAsync(ConnectionKey key, CancellationToken cancellationToken = default)
    {
        key.Validate();
        await using var connection = await OpenAsync(cancellationToken);
        using var command = Command(connection, key, """
            SELECT payload FROM connector_credentials WHERE tenant_id = $tenant AND connection_id = $connection;
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return Read(reader, key.TenantId, StoredCredential.ConnectionId(key), 0).Credential;
    }

    public async ValueTask SetAsync(ConnectionKey key, ConnectorCredential credential, CancellationToken cancellationToken = default)
    {
        key.Validate();
        var payload = Protector(key.TenantId, StoredCredential.ConnectionId(key))
            .Protect(new StoredCredential(key, credential).Serialize());
        if (payload.Length > StoredCredential.MaximumBytes)
            throw new InvalidOperationException("Encrypted credential exceeds the sample storage limit.");
        await using var connection = await OpenAsync(cancellationToken);
        using var command = Command(connection, key, """
            INSERT INTO connector_credentials (tenant_id, connection_id, payload) VALUES ($tenant, $connection, $payload)
            ON CONFLICT (tenant_id, connection_id) DO UPDATE SET payload = excluded.payload;
            """);
        command.Parameters.Add("$payload", SqliteType.Blob).Value = payload;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask DeleteAsync(ConnectionKey key, CancellationToken cancellationToken = default)
    {
        key.Validate();
        await using var connection = await OpenAsync(cancellationToken);
        using var command = Command(connection, key, """
            DELETE FROM connector_credentials WHERE tenant_id = $tenant AND connection_id = $connection;
            """);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async IAsyncEnumerable<ConnectionInfo> ListAsync(string tenantId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        StoredCredential.ValidateTenant(tenantId);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT connection_id, payload FROM connector_credentials WHERE tenant_id = $tenant ORDER BY connection_id;";
        command.Parameters.AddWithValue("$tenant", StoredCredential.Hash(tenantId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            yield return Read(reader, tenantId, reader.GetString(0), 1).Info;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static SqliteCommand Command(SqliteConnection connection, ConnectionKey key, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$tenant", StoredCredential.Hash(key.TenantId));
        command.Parameters.AddWithValue("$connection", StoredCredential.ConnectionId(key));
        return command;
    }

    private IDataProtector Protector(string tenantId, string connectionId) =>
        protection.CreateProtector("AgentConnector.StorageSample.Sqlite.v1", StoredCredential.Hash(tenantId), connectionId);

    private StoredCredential Read(SqliteDataReader reader, string tenantId, string connectionId, int ordinal)
    {
        var length = reader.GetBytes(ordinal, 0, null, 0, 0);
        if (length > StoredCredential.MaximumBytes) throw new InvalidOperationException("Stored credential exceeds the sample storage limit.");
        var payload = new byte[checked((int)length)];
        reader.GetBytes(ordinal, 0, payload, 0, payload.Length);
        var record = StoredCredential.Deserialize(Protector(tenantId, connectionId).Unprotect(payload));
        record.RequireIdentity(tenantId, connectionId);
        return record;
    }
}
