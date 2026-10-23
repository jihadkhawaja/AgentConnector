using System.Net;
using System.Text.Json.Nodes;
using AgentConnector;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentConnector.Tests;

public sealed class OAuthAndRuntimeTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "connector-runtime-" + Guid.NewGuid().ToString("N"));
    private readonly ConnectionCoordinator coordinator = new();
    private readonly ConnectorOptions options;
    private readonly ProviderCatalog catalog;
    private readonly ICredentialStore store;
    private readonly StubHandler handler = new();
    private readonly HttpClient client;
    private readonly ConnectorOAuth oauth;
    private readonly ConnectorRuntime runtime;

    public OAuthAndRuntimeTests()
    {
        options = new() { CredentialDirectory = directory };
        options.AllowedActions.Add("*");
        options.OAuthClients["github"] = new() { ClientId = "test-client", ClientSecret = "client-secret", RedirectUri = new("https://app.example/connector/oauth/callback"), Scopes = ["read:user"] };
        options.OAuthClients["notion"] = new() { ClientId = "notion-client", ClientSecret = "notion-secret", RedirectUri = new("https://app.example/connector/oauth/callback") };
        catalog = new(options);
        store = new EncryptedFileCredentialStore(options, new EphemeralDataProtectionProvider());
        client = new(handler);
        var transport = new ConnectorTransport(client, options);
        oauth = new(options, catalog, store, coordinator, transport);
        runtime = new(options, catalog, new(options), store, coordinator, oauth, transport,
            new LoggingConnectorAuditSink(NullLogger<LoggingConnectorAuditSink>.Instance), NullLogger<ConnectorRuntime>.Instance);
    }

    [Fact]
    public async Task ConnectionServiceManagesCredentialsWithoutExecutingRequests()
    {
        var connections = new ConnectorConnectionService(catalog, store, coordinator, oauth);
        var key = new ConnectionKey("tenant", "github", "work");
        var credential = new ConnectorCredential(CredentialKind.Bearer, "connection-secret");

        var info = await connections.SetAsync(key, credential);
        Assert.Equal(new ConnectionInfo("github", "work", CredentialKind.Bearer, null), info);
        Assert.Equal(credential, await store.GetAsync(key));

        var listed = new List<ConnectionInfo>();
        await foreach (var connection in connections.ListAsync(key.TenantId)) listed.Add(connection);
        Assert.Equal(info, Assert.Single(listed));

        await connections.DeleteAsync(key);
        Assert.Null(await store.GetAsync(key));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task ReplacingConnectionFencesPendingOAuthCallback()
    {
        var key = new ConnectionKey("tenant", "github");
        var authorization = await oauth.BeginAsync(key);
        var connections = new ConnectorConnectionService(catalog, store, coordinator, oauth);
        await connections.SetAsync(key, new(CredentialKind.Bearer, "replacement-secret"));

        var state = QueryHelpers.ParseQuery(authorization.AuthorizationUri.Query)["state"].ToString();
        Assert.Equal("stale_oauth_state", (await Assert.ThrowsAsync<ConnectorException>(() => oauth.CompleteAsync(state, "code"))).Code);
        Assert.Equal("replacement-secret", (await store.GetAsync(key))!.Secret);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task ExpiredNonRefreshableCredentialsNeverDispatch()
    {
        await runtime.SetConnectionAsync(new("tenant", "github"),
            new(CredentialKind.Bearer, "expired-secret", ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(-1)));

        var exception = await Assert.ThrowsAsync<ConnectorException>(() => runtime.ExecuteAsync("tenant", "github.get_current_user", new()));
        Assert.Equal("credential_expired", exception.Code);
        Assert.Equal(HttpStatusCode.Unauthorized, exception.Status);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task OAuthUsesPkceSingleUseStateAndDoesNotReturnSecrets()
    {
        var authorization = await oauth.BeginAsync(new("tenant", "github", "work"));
        var query = QueryHelpers.ParseQuery(authorization.AuthorizationUri.Query);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.False(query.ContainsKey("client_secret"));
        handler.Responder = async request =>
        {
            var fields = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync());
            Assert.Equal(query["code_challenge"].ToString(), WebEncoders.Base64UrlEncode(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(fields["code_verifier"].ToString()))));
            return Json("""{"access_token":"provider-secret","token_type":"bearer","refresh_token":"refresh-secret","expires_in":3600}""");
        };
        var state = query["state"].ToString();
        var connection = await oauth.CompleteAsync(state, "test-code");
        Assert.DoesNotContain("provider-secret", connection.ToString());
        Assert.Equal("provider-secret", (await store.GetAsync(new("tenant", "github", "work")))!.Secret);
        Assert.Equal("invalid_oauth_state", (await Assert.ThrowsAsync<ConnectorException>(() => oauth.CompleteAsync(state, "test-code"))).Code);
    }

    [Fact]
    public async Task DisconnectFencesPendingOAuthCallback()
    {
        var key = new ConnectionKey("tenant", "github");
        var authorization = await oauth.BeginAsync(key);
        await runtime.DeleteConnectionAsync(key);
        var state = QueryHelpers.ParseQuery(authorization.AuthorizationUri.Query)["state"].ToString();
        Assert.Equal("stale_oauth_state", (await Assert.ThrowsAsync<ConnectorException>(() => oauth.CompleteAsync(state, "code"))).Code);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task NotionUsesBasicClientAuthAndJsonTokenExchange()
    {
        var authorization = await oauth.BeginAsync(new("tenant", "notion"));
        handler.Responder = async request =>
        {
            Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
            Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
            var fields = JsonNode.Parse(await request.Content.ReadAsStringAsync())!;
            Assert.Equal("authorization_code", fields["grant_type"]!.GetValue<string>());
            Assert.Null(fields["client_secret"]);
            return Json("""{"access_token":"notion-token","token_type":"bearer"}""");
        };
        await oauth.CompleteAsync(QueryHelpers.ParseQuery(authorization.AuthorizationUri.Query)["state"].ToString(), "code");
    }

    [Fact]
    public async Task ConcurrentCallsRefreshOnlyOnceAndPreserveRefreshToken()
    {
        var key = new ConnectionKey("tenant", "github");
        await runtime.SetConnectionAsync(key, new(CredentialKind.OAuth2, "expired", RefreshToken: "refresh-token", ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(-1)));
        var refreshes = 0;
        handler.Responder = request =>
        {
            if (request.RequestUri!.Host == "github.com")
            {
                Interlocked.Increment(ref refreshes);
                return Task.FromResult(Json("""{"access_token":"new-token","token_type":"bearer","expires_in":3600}"""));
            }
            Assert.Equal("new-token", request.Headers.Authorization!.Parameter);
            return Task.FromResult(Json("""{"login":"example"}"""));
        };
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => runtime.ExecuteAsync("tenant", "github.get_current_user", new())));
        Assert.Equal(1, refreshes);
        Assert.Equal("refresh-token", (await store.GetAsync(key))!.RefreshToken);
    }

    [Fact]
    public async Task DenialAndMissingAliasNeverDispatchOrFallback()
    {
        await runtime.SetConnectionAsync(new("tenant", "github"), new(CredentialKind.Bearer, "default-secret"));
        options.BlockedActions.Add("github.get_current_user");
        Assert.Equal("action_not_allowed", (await Assert.ThrowsAsync<ConnectorException>(() => runtime.ExecuteAsync("tenant", "github.get_current_user", new()))).Code);
        options.BlockedActions.Clear();
        Assert.Equal("connection_not_found", (await Assert.ThrowsAsync<ConnectorException>(() => runtime.ExecuteAsync("tenant", "github.get_current_user", new(), "missing"))).Code);
        Assert.Equal("connection_not_found", (await Assert.ThrowsAsync<ConnectorException>(() => runtime.ExecuteAsync("other-tenant", "github.get_current_user", new()))).Code);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task ProviderErrorsAreSanitizedAndNotRetried()
    {
        await runtime.SetConnectionAsync(new("tenant", "github"), new(CredentialKind.Bearer, "sensitive-secret"));
        handler.Responder = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("sensitive-secret") });
        var exception = await Assert.ThrowsAsync<ConnectorException>(() => runtime.ExecuteAsync("tenant", "github.get_current_user", new()));
        Assert.DoesNotContain("sensitive-secret", exception.ToString());
        Assert.Equal(1, handler.Calls);
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    public void Dispose()
    {
        client.Dispose();
        coordinator.Dispose();
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public int Calls;
        public Func<HttpRequestMessage, Task<HttpResponseMessage>> Responder { get; set; } = _ => Task.FromResult(Json("{}"));
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Responder(request);
        }
    }
}