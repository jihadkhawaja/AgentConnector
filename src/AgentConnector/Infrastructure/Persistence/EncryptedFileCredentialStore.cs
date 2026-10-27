using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace AgentConnector;

public sealed class EncryptedFileCredentialStore(ConnectorOptions options, IDataProtectionProvider protection) : ICredentialStore
{
    private sealed record StoredConnection(ConnectionKey Key, ConnectorCredential Credential);

    public async ValueTask<ConnectorCredential?> GetAsync(ConnectionKey key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        var path = GetPath(key);
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            var stored = JsonSerializer.Deserialize<StoredConnection>(Protector(key.TenantId, path).Unprotect(bytes))
                ?? throw new CryptographicException("Invalid credential record.");
            if (stored.Key != key)
                throw new CryptographicException("Credential identity does not match.");
            return stored.Credential;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public async ValueTask SetAsync(ConnectionKey key, ConnectorCredential credential, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        var path = GetPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = Protector(key.TenantId, path).Protect(JsonSerializer.SerializeToUtf8Bytes(new StoredConnection(key, credential)));
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally { File.Delete(temporaryPath); }
    }

    public ValueTask DeleteAsync(ConnectionKey key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(key);
        try { File.Delete(GetPath(key)); }
        catch (DirectoryNotFoundException) { /* Deleting an already absent record is idempotent. */ }
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<ConnectionInfo> ListAsync(string tenantId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ConnectionKey.ValidateIdentity(tenantId);
        var directory = Path.Combine(options.CredentialDirectory, Hash(tenantId));
        if (!Directory.Exists(directory)) yield break;
        foreach (var path in Directory.EnumerateFiles(directory, "*.credential"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] bytes;
            try { bytes = await File.ReadAllBytesAsync(path, cancellationToken); }
            catch (FileNotFoundException) { continue; }
            var stored = JsonSerializer.Deserialize<StoredConnection>(Protector(tenantId, path).Unprotect(bytes))
                ?? throw new CryptographicException("Invalid credential record.");
            if (!StringComparer.Ordinal.Equals(stored.Key.TenantId, tenantId))
                throw new CryptographicException("Credential tenant does not match.");
            yield return new(stored.Key.ProviderId, stored.Key.Name, stored.Credential.Kind, stored.Credential.ExpiresAt);
        }
    }

    public static void ValidateKey(ConnectionKey key) => key.Validate();

    private string GetPath(ConnectionKey key) => Path.Combine(options.CredentialDirectory,
        Hash(key.TenantId), Hash(JsonSerializer.Serialize(new[] { key.ProviderId, key.Name })) + ".credential");

    private IDataProtector Protector(string tenantId, string path) => protection.CreateProtector(
        "AgentConnector.Credentials.v1", Hash(tenantId), Path.GetFileName(path));

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
