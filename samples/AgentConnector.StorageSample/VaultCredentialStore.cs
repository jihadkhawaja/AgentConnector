using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentConnector.StorageSample;

/// <summary>Sample HashiCorp Vault KV v2 store. Vault owns encryption at rest.</summary>
public sealed class VaultCredentialStore : ICredentialStore
{
    public const string HttpClientName = "AgentConnector.VaultCredentials";
    private readonly IHttpClientFactory clients;
    private readonly VaultStoreOptions options;

    public VaultCredentialStore(IHttpClientFactory clients, VaultStoreOptions options)
    {
        options.Validate();
        this.clients = clients;
        this.options = options;
    }

    public async ValueTask<ConnectorCredential?> GetAsync(ConnectionKey key, CancellationToken cancellationToken = default)
    {
        key.Validate();
        var record = await GetRecordAsync(key.TenantId, StoredCredential.ConnectionId(key), cancellationToken);
        return record?.Credential;
    }

    public async ValueTask SetAsync(ConnectionKey key, ConnectorCredential credential, CancellationToken cancellationToken = default)
    {
        key.Validate();
        var bytes = new StoredCredential(key, credential).Serialize();
        var body = new JsonObject { ["data"] = JsonNode.Parse(bytes) };
        await SendAsync(HttpMethod.Post, PathFor("data", key.TenantId, StoredCredential.ConnectionId(key)), body, cancellationToken);
    }

    public async ValueTask DeleteAsync(ConnectionKey key, CancellationToken cancellationToken = default)
    {
        key.Validate();
        // Delete metadata so old KV versions cannot restore a disconnected credential.
        await SendAsync(HttpMethod.Delete, PathFor("metadata", key.TenantId, StoredCredential.ConnectionId(key)), null, cancellationToken);
    }

    public async IAsyncEnumerable<ConnectionInfo> ListAsync(string tenantId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        StoredCredential.ValidateTenant(tenantId);
        var result = await SendAsync(new HttpMethod("LIST"), PathFor("metadata", tenantId), null, cancellationToken);
        if (result is null) yield break;
        if (result["data"]?["keys"] is not JsonArray keys || keys.Count > 1000)
            throw new InvalidOperationException("Vault returned an invalid or oversized connection list.");
        foreach (var item in keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = item?.GetValue<string>();
            if (id is null || id.Length != 64 || id.Any(character => !char.IsAsciiHexDigitUpper(character)))
                throw new InvalidOperationException("Vault returned an invalid connection identifier.");
            var record = await GetRecordAsync(tenantId, id, cancellationToken);
            if (record is not null) yield return record.Info;
        }
    }

    private async Task<StoredCredential?> GetRecordAsync(string tenantId, string connectionId, CancellationToken cancellationToken)
    {
        var result = await SendAsync(HttpMethod.Get, PathFor("data", tenantId, connectionId), null, cancellationToken);
        if (result is null) return null;
        var data = result["data"]?["data"] ?? throw new InvalidOperationException("Vault returned an invalid credential record.");
        var record = StoredCredential.Deserialize(JsonSerializer.SerializeToUtf8Bytes(data, StoredCredential.JsonOptions));
        record.RequireIdentity(tenantId, connectionId);
        return record;
    }

    private string PathFor(string operation, string tenantId, string? connectionId = null) =>
        $"v1/{options.Mount}/{operation}/agentconnector/{StoredCredential.Hash(tenantId)}/" + connectionId;

    private async Task<JsonNode?> SendAsync(HttpMethod method, string path, JsonObject? body, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(method, new Uri(options.Address, path));
        request.Headers.Add("X-Vault-Token", options.Token);
        if (body is not null)
        {
            request.Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body, StoredCredential.JsonOptions));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        using var client = clients.CreateClient(HttpClientName);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound && (method == HttpMethod.Get || method.Method == "LIST" || method == HttpMethod.Delete))
            return null;
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Vault credential operation failed; check service access and availability.");
        if (method == HttpMethod.Delete || response.StatusCode == HttpStatusCode.NoContent) return null;
        if (response.Content.Headers.ContentLength > StoredCredential.MaximumBytes)
            throw new InvalidOperationException("Vault response exceeds the sample storage limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, timeout.Token)) > 0)
        {
            if (buffer.Length + read > StoredCredential.MaximumBytes)
                throw new InvalidOperationException("Vault response exceeds the sample storage limit.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), timeout.Token);
        }
        try
        {
            using var document = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions
            {
                MaxDepth = 32,
                AllowDuplicateProperties = false
            });
            return JsonNode.Parse(document.RootElement.GetRawText())
                ?? throw new InvalidOperationException("Vault returned an empty response.");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Vault returned invalid JSON.");
        }
    }
}
