using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgentConnector.StorageSample;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;

namespace AgentConnector.Tests;

public sealed class SampleCredentialStoreTests
{
    private static readonly ConnectorCredential Original = new(CredentialKind.OAuth2, "synthetic-access-token",
        RefreshToken: "synthetic-refresh-token", ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(30), Scopes: ["read:user"]);

    [Fact]
    public async Task SqlitePersistsEncryptedCredentialsAndIsolatesTenantsAndNames()
    {
        using var fixture = new SqliteFixture();
        var store = fixture.Store;
        await store.InitializeAsync();
        var key = new ConnectionKey("tenant'; DROP TABLE connector_credentials;--", "github", "work");
        await store.SetAsync(key, Original);
        await store.SetAsync(key with { TenantId = "other" }, Original with { Secret = "other-tenant-secret" });
        await store.SetAsync(key with { Name = "personal" }, Original with { Secret = "other-name-secret" });

        AssertCredential(Original, await store.GetAsync(key));
        Assert.Null(await store.GetAsync(key with { Name = "absent" }));
        var entries = await CollectAsync(store.ListAsync(key.TenantId));
        Assert.Equal(new[] { "personal", "work" }, entries.Select(entry => entry.Name).Order().ToArray());

        var refreshed = Original with { Secret = "refreshed-access-token", RefreshToken = "rotated-refresh-token" };
        await store.SetAsync(key, refreshed);
        var reopened = new SqliteCredentialStore(fixture.Path, fixture.Protection);
        AssertCredential(refreshed, await reopened.GetAsync(key));
        Assert.Equal("other-tenant-secret", (await reopened.GetAsync(key with { TenantId = "other" }))!.Secret);
        Assert.Equal("other-name-secret", (await reopened.GetAsync(key with { Name = "personal" }))!.Secret);

        await using var connection = new SqliteConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM connector_credentials;";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var payload = Encoding.UTF8.GetString((byte[])reader[0]);
            Assert.DoesNotContain("access-token", payload, StringComparison.Ordinal);
            Assert.DoesNotContain("refresh-token", payload, StringComparison.Ordinal);
            Assert.DoesNotContain("other-tenant-secret", payload, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task SqliteRejectsCiphertextCopiedToAnotherIdentity()
    {
        using var fixture = new SqliteFixture();
        await fixture.Store.InitializeAsync();
        var first = new ConnectionKey("first", "github");
        var second = new ConnectionKey("second", "github", "work");
        await fixture.Store.SetAsync(first, Original);
        await fixture.Store.SetAsync(second, Original);
        await using var connection = new SqliteConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE connector_credentials SET payload = (SELECT payload FROM connector_credentials LIMIT 1);";
        await command.ExecuteNonQueryAsync();
        var errors = new[]
        {
            await Record.ExceptionAsync(async () => await fixture.Store.GetAsync(first)),
            await Record.ExceptionAsync(async () => await fixture.Store.GetAsync(second))
        };
        Assert.Single(errors.OfType<CryptographicException>());
    }

    [Fact]
    public async Task SqliteDeletesOnlySelectedConnectionAndHonorsCancellation()
    {
        using var fixture = new SqliteFixture();
        await fixture.Store.InitializeAsync();
        var key = new ConnectionKey("tenant", "github");
        await fixture.Store.SetAsync(key, Original);
        await fixture.Store.SetAsync(key with { Name = "other" }, Original);
        await fixture.Store.DeleteAsync(key);
        await fixture.Store.DeleteAsync(key);
        Assert.Null(await fixture.Store.GetAsync(key));
        Assert.Single(await CollectAsync(fixture.Store.ListAsync(key.TenantId)));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await fixture.Store.GetAsync(key, cancellation.Token));
        await Assert.ThrowsAsync<ConnectorException>(async () => await fixture.Store.SetAsync(key with { TenantId = "" }, Original));
    }

    [Fact]
    public async Task VaultRoundTripsRefreshesListsAndPermanentlyDeletesConnections()
    {
        using var fixture = new VaultFixture();
        var key = new ConnectionKey("tenant/../sensitive", "github", "work");
        await fixture.Store.SetAsync(key, Original);
        await fixture.Store.SetAsync(key with { TenantId = "other" }, Original);
        await fixture.Store.SetAsync(key with { Name = "personal" }, Original);
        AssertCredential(Original, await fixture.Store.GetAsync(key));
        var entries = await CollectAsync(fixture.Store.ListAsync(key.TenantId));
        Assert.Equal(new[] { "personal", "work" }, entries.Select(entry => entry.Name).Order().ToArray());
        var refreshed = Original with { Secret = "refreshed-vault-access", RefreshToken = "rotated-vault-refresh" };
        await fixture.Store.SetAsync(key, refreshed);
        AssertCredential(refreshed, await fixture.Store.GetAsync(key));
        await fixture.Store.DeleteAsync(key);
        await fixture.Store.DeleteAsync(key);
        Assert.Null(await fixture.Store.GetAsync(key));
        Assert.Single(await CollectAsync(fixture.Store.ListAsync(key.TenantId)));
        Assert.NotNull(await fixture.Store.GetAsync(key with { TenantId = "other" }));
        Assert.Contains(fixture.Handler.Paths, path => path.StartsWith("DELETE /v1/secret/metadata/agentconnector/", StringComparison.Ordinal));
        Assert.All(fixture.Handler.Paths, path => Assert.DoesNotContain("sensitive", path, StringComparison.Ordinal));
    }

    [Fact]
    public async Task VaultRejectsRecordsReturnedForAnotherTenant()
    {
        using var fixture = new VaultFixture();
        var key = new ConnectionKey("tenant", "github");
        await fixture.Store.SetAsync(key, Original);
        var record = fixture.Handler.Records.Values.Single();
        record["key"]!["tenantId"] = "another-tenant";
        await Assert.ThrowsAsync<CryptographicException>(async () => await fixture.Store.GetAsync(key));
        await Assert.ThrowsAsync<CryptographicException>(async () => await CollectAsync(fixture.Store.ListAsync(key.TenantId)));
    }

    [Theory]
    [InlineData("https://vault.example.test/", false, true)]
    [InlineData("http://127.0.0.1:8200/", true, true)]
    [InlineData("http://127.0.0.1:8200/", false, false)]
    [InlineData("http://vault.example.test/", true, false)]
    [InlineData("https://user:password@vault.example.test/", false, false)]
    [InlineData("https://vault.example.test/path/", false, false)]
    [InlineData("https://vault.example.test/?token=test", false, false)]
    [InlineData("https://vault.example.test/#fragment", false, false)]
    public void VaultValidatesTrustedOrigin(string address, bool allowHttp, bool valid)
    {
        var options = new VaultStoreOptions(new Uri(address), "secret", "synthetic-vault-token", allowHttp);
        if (valid) options.Validate();
        else Assert.Throws<ArgumentException>(options.Validate);
        Assert.DoesNotContain("synthetic-vault-token", options.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../secret", "synthetic-token")]
    [InlineData("secret/path", "synthetic-token")]
    [InlineData("secret", "token\r\nheader")]
    [InlineData("secret", "")]
    public void VaultRejectsUnsafeMountsAndTokens(string mount, string token) =>
        Assert.Throws<ArgumentException>(() => new VaultStoreOptions(new Uri("https://vault.example.test"), mount, token).Validate());

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Redirect)]
    public async Task VaultDiscardsErrorsAndRedirectBodies(HttpStatusCode status)
    {
        using var fixture = new VaultFixture();
        fixture.Handler.Override = () => new HttpResponseMessage(status) { Content = new StringContent("synthetic-sensitive-provider-error") };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Store.GetAsync(new("tenant", "github")));
        Assert.DoesNotContain("synthetic-sensitive", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"data\":{},\"data\":{}}")]
    [InlineData("not-json-synthetic-sensitive-value")]
    public async Task VaultRejectsInvalidOrDuplicateJsonWithoutLeakingValues(string body)
    {
        using var fixture = new VaultFixture();
        fixture.Handler.Override = () => JsonResponse(body);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Store.GetAsync(new("tenant", "github")));
        Assert.DoesNotContain("synthetic-sensitive", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task VaultBoundsResponseAndListSizesAndHonorsCancellation()
    {
        using var fixture = new VaultFixture();
        fixture.Handler.Override = () => JsonResponse(new string('x', 1024 * 1024 + 1));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Store.GetAsync(new("tenant", "github")));
        fixture.Handler.Override = () => JsonResponse(JsonSerializer.Serialize(new { data = new { keys = Enumerable.Repeat("A", 1001) } }));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await CollectAsync(fixture.Store.ListAsync("tenant")));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await fixture.Store.GetAsync(new("tenant", "github"), cancellation.Token));
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static void AssertCredential(ConnectorCredential expected, ConnectorCredential? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected with { Scopes = null }, actual with { Scopes = null });
        Assert.Equal(expected.Scopes?.ToArray(), actual.Scopes?.ToArray());
    }

    private static async Task<List<ConnectionInfo>> CollectAsync(IAsyncEnumerable<ConnectionInfo> source)
    {
        var entries = new List<ConnectionInfo>();
        await foreach (var entry in source) entries.Add(entry);
        return entries;
    }

    private sealed class SqliteFixture : IDisposable
    {
        private readonly string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "agentconnector-storage-" + Guid.NewGuid().ToString("N"));
        internal string Path => System.IO.Path.Combine(directory, "credentials.db");
        internal IDataProtectionProvider Protection { get; } = new EphemeralDataProtectionProvider();
        internal SqliteCredentialStore Store { get; }
        internal string ConnectionString => new SqliteConnectionStringBuilder { DataSource = Path, Pooling = false }.ToString();

        internal SqliteFixture()
        {
            Directory.CreateDirectory(directory);
            Store = new(Path, Protection);
        }

        public void Dispose() => Directory.Delete(directory, true);
    }

    private sealed class VaultFixture : IHttpClientFactory, IDisposable
    {
        internal FakeVaultHandler Handler { get; } = new();
        internal VaultCredentialStore Store { get; }
        internal VaultFixture() => Store = new(this, new(new Uri("https://vault.example.test"), "secret", "synthetic-vault-token"));
        public HttpClient CreateClient(string name) => new(Handler, disposeHandler: false);
        public void Dispose() => Handler.Dispose();
    }

    private sealed class FakeVaultHandler : HttpMessageHandler
    {
        internal Dictionary<string, JsonNode> Records { get; } = new(StringComparer.Ordinal);
        internal List<string> Paths { get; } = [];
        internal Func<HttpResponseMessage>? Override { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("synthetic-vault-token", request.Headers.GetValues("X-Vault-Token").Single());
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add($"{request.Method.Method} {path}");
            if (Override is not null) return Override();
            var dataPath = path.Replace("/metadata/", "/data/", StringComparison.Ordinal);
            if (request.Method == HttpMethod.Post)
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!;
                Records[dataPath] = body["data"]!.DeepClone();
                return JsonResponse("{\"data\":{\"version\":1}}");
            }
            if (request.Method == HttpMethod.Delete)
            {
                Assert.Contains("/metadata/", path, StringComparison.Ordinal);
                Records.Remove(dataPath);
                return new(HttpStatusCode.NoContent);
            }
            if (request.Method.Method == "LIST")
            {
                var keys = Records.Keys.Where(key => key.StartsWith(dataPath, StringComparison.Ordinal)).Select(key => key[dataPath.Length..]).ToArray();
                return JsonResponse(JsonSerializer.Serialize(new { data = new { keys } }));
            }
            return Records.TryGetValue(dataPath, out var record)
                ? JsonResponse(new JsonObject { ["data"] = new JsonObject { ["data"] = record.DeepClone() } }.ToJsonString())
                : new(HttpStatusCode.NotFound);
        }
    }
}
