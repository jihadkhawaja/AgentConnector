using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AgentConnector.StorageSample;

internal sealed record StoredCredential(ConnectionKey Key, ConnectorCredential Credential)
{
    internal const int MaximumBytes = 1024 * 1024;
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 32,
        AllowDuplicateProperties = false
    };

    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    internal static string ConnectionId(ConnectionKey key) => Hash(JsonSerializer.Serialize(new[] { key.ProviderId, key.Name }));
    internal static void ValidateTenant(string tenantId) => new ConnectionKey(tenantId, "validation").Validate();
    internal ConnectionInfo Info => new(Key.ProviderId, Key.Name, Credential.Kind, Credential.ExpiresAt);

    internal byte[] Serialize()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);
        if (bytes.Length > MaximumBytes) throw new InvalidOperationException("Credential record exceeds the sample storage limit.");
        return bytes;
    }

    internal static StoredCredential Deserialize(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaximumBytes) throw new InvalidOperationException("Credential record exceeds the sample storage limit.");
        try
        {
            var record = JsonSerializer.Deserialize<StoredCredential>(bytes, JsonOptions);
            if (record?.Key is null || record.Credential is null)
                throw new InvalidOperationException("Credential record is invalid.");
            record.Key.Validate();
            return record;
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Credential record is invalid.");
        }
    }

    internal void RequireIdentity(string tenantId, string connectionId)
    {
        if (!StringComparer.Ordinal.Equals(Key.TenantId, tenantId) ||
            !StringComparer.Ordinal.Equals(ConnectionId(Key), connectionId))
            throw new CryptographicException("Credential identity does not match.");
    }
}
