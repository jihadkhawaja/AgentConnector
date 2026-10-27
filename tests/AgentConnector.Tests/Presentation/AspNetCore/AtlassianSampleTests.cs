using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using AgentConnector;
using AgentConnector.Sample;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentConnector.Tests;

public sealed class AtlassianSampleTests : IAsyncLifetime
{
    private const string CloudId = "00000000-0000-4000-8000-000000000001";
    private readonly string directory = Path.Combine(Path.GetTempPath(), "connector-atlassian-" + Guid.NewGuid().ToString("N"));
    private readonly AtlassianHandler handler = new();
    private WebApplication app = null!;
    private HttpClient client = null!;

    private static Dictionary<string, string?> Settings() => new()
    {
        ["Atlassian:ClientId"] = "synthetic-client", ["Atlassian:ClientSecret"] = "synthetic-secret",
        ["Atlassian:CloudId"] = CloudId, ["Atlassian:PublicOrigin"] = "https://localhost:7188"
    };

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(Settings());
        builder.WebHost.UseTestServer();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, SampleAuthenticationHandler>("test", _ => { });
        builder.Services.AddAgentConnector(options =>
        {
            options.CredentialDirectory = directory;
            AtlassianSample.Configure(options, builder.Configuration);
        });
        builder.Services.AddHttpClient<ConnectorTransport>().ConfigurePrimaryHttpMessageHandler(() => handler);
        app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapAtlassianSample();
        app.MapAgentConnector();
        await app.StartAsync();
        client = app.GetTestClient();
    }

    [Theory]
    [InlineData("/atlassian")]
    [InlineData("/atlassian/")]
    public async Task SignInPageRedirectAcceptsEitherTrailingSlash(string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/atlassian/index.html", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public void ConfigurationIsOptionalAndRejectsPartialOrUnsafeSettings()
    {
        Assert.False(AtlassianSample.Configure(new(), new ConfigurationBuilder().Build()));
        foreach (var field in Settings().Keys)
        {
            var missing = Settings();
            missing.Remove(field);
            Assert.Throws<InvalidOperationException>(() => AtlassianSample.Configure(new(), new ConfigurationBuilder().AddInMemoryCollection(missing).Build()));
        }
        foreach (var origin in new[] { "http://app.example", "https://app.example/path", "https://user@app.example", "https://app.example/?query=1", "https://app.example/#fragment" })
        {
            var invalid = Settings();
            invalid["Atlassian:PublicOrigin"] = origin;
            Assert.Throws<InvalidOperationException>(() => AtlassianSample.Configure(new(), new ConfigurationBuilder().AddInMemoryCollection(invalid).Build()));
        }
        var invalidCloud = Settings();
        invalidCloud["Atlassian:CloudId"] = "../other-site";
        Assert.Throws<InvalidOperationException>(() => AtlassianSample.Configure(new(), new ConfigurationBuilder().AddInMemoryCollection(invalidCloud).Build()));
    }

    [Theory]
    [InlineData("jira", "jira.get_issue", "rest/api/3/issue/DEMO-1", "issue_id", "DEMO-1")]
    [InlineData("confluence", "confluence.get_page", "wiki/api/v2/pages/123", "page_id", "123")]
    public async Task PopupCallbackExchangesJsonOnceAndAgentsUseThePinnedSite(string provider, string action, string path, string inputName, string inputValue)
    {
        client.DefaultRequestHeaders.Authorization = new("Bearer", "admin");
        var begin = await client.PostAsJsonAsync($"/connector/api/oauth/{provider}/authorize", new { connectionName = "work" });
        begin.EnsureSuccessStatusCode();
        var authorization = new Uri(JsonNode.Parse(await begin.Content.ReadAsStringAsync())!["data"]!["authorizationUri"]!.GetValue<string>());
        Assert.Equal("https://auth.atlassian.com/authorize", authorization.GetLeftPart(UriPartial.Path));
        var query = QueryHelpers.ParseQuery(authorization.Query);
        Assert.Equal("api.atlassian.com", query["audience"].ToString());
        Assert.Equal("consent", query["prompt"].ToString());
        Assert.Equal("https://localhost:7188/atlassian/oauth/callback", query["redirect_uri"].ToString());
        Assert.Contains("offline_access", query["scope"].ToString());
        Assert.Contains("read:jira-work", query["scope"].ToString());
        Assert.Contains("read:page:confluence", query["scope"].ToString());
        Assert.False(query.ContainsKey("code_challenge"));

        // Browsers return from Atlassian without a connector bearer token.
        client.DefaultRequestHeaders.Authorization = null;
        var callbackPath = QueryHelpers.AddQueryString("/atlassian/oauth/callback", new Dictionary<string, string?>
        {
            ["state"] = query["state"].ToString(), ["code"] = "synthetic-code"
        });
        var callback = await client.GetAsync(callbackPath);
        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        var html = await callback.Content.ReadAsStringAsync();
        Assert.Contains("data-success=\"true\"", html);
        Assert.Contains("/atlassian/callback.js", html);
        Assert.DoesNotContain("synthetic-code", html);
        Assert.DoesNotContain("synthetic-access", html);
        Assert.True(callback.Headers.CacheControl!.NoStore);
        Assert.Equal("no-referrer", Assert.Single(callback.Headers.GetValues("Referrer-Policy")));
        Assert.Equal("unsafe-none", Assert.Single(callback.Headers.GetValues("Cross-Origin-Opener-Policy")));
        Assert.Contains("frame-ancestors 'none'", Assert.Single(callback.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(callbackPath)).StatusCode);
        Assert.Equal(1, handler.Exchanges);

        client.DefaultRequestHeaders.Authorization = new("Bearer", "runtime");
        var execution = await client.PostAsJsonAsync($"/connector/v1/actions/{action}", new
        {
            connectionName = "work", input = new Dictionary<string, string> { [inputName] = inputValue }
        });
        Assert.Equal(HttpStatusCode.OK, execution.StatusCode);
        Assert.Equal($"https://api.atlassian.com/ex/{provider}/{CloudId}/{path}", handler.LastApiUri!.AbsoluteUri);
        var other = await client.PostAsJsonAsync($"/connector/v1/actions/{action}", new
        {
            connectionName = "missing", input = new Dictionary<string, string> { [inputName] = inputValue }
        });
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
    }

    [Fact]
    public async Task RuntimeCannotStartSignInOrWriteAndInvalidCallbacksNeverExchangeTokens()
    {
        client.DefaultRequestHeaders.Authorization = new("Bearer", "runtime");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/connector/api/oauth/jira/authorize", new { })).StatusCode);
        foreach (var action in new[] { "jira.create_issue", "confluence.create_page" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/connector/v1/actions/{action}", new { input = new { body = new { } } })).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        var invalid = await client.GetAsync("/atlassian/oauth/callback?state=invalid&code=secret-code&error_description=untrusted-details");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var html = await invalid.Content.ReadAsStringAsync();
        Assert.Contains("data-success=\"false\"", html);
        Assert.DoesNotContain("secret-code", html);
        Assert.DoesNotContain("untrusted-details", html);
        Assert.Equal(0, handler.Exchanges);
    }

    [Fact]
    public async Task ConsentDenialConsumesStateWithoutExchangingTokens()
    {
        var oauth = app.Services.GetRequiredService<ConnectorOAuth>();
        var authorization = await oauth.BeginAsync(new("sample-tenant", "confluence", "work"));
        var state = QueryHelpers.ParseQuery(authorization.AuthorizationUri.Query)["state"].ToString();
        var denied = await client.GetAsync(QueryHelpers.AddQueryString("/atlassian/oauth/callback", new Dictionary<string, string?>
        {
            ["state"] = state, ["error"] = "access_denied"
        }));
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        Assert.Equal("invalid_oauth_state", (await Assert.ThrowsAsync<ConnectorException>(() => oauth.CompleteAsync(state, "synthetic-code"))).Code);
        Assert.Equal(0, handler.Exchanges);
    }

    [Fact]
    public async Task RefreshUsesJsonAndPersistsTheRotatedRefreshToken()
    {
        var key = new ConnectionKey("sample-tenant", "jira", "work");
        var connections = app.Services.GetRequiredService<ConnectorConnectionService>();
        await connections.SetAsync(key, new(CredentialKind.OAuth2, "expired", RefreshToken: "synthetic-refresh", ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(-1)));
        var runtime = app.Services.GetRequiredService<ConnectorRuntime>();
        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => runtime.ExecuteAsync(key.TenantId, "jira.get_current_user", new(), key.Name)));
        Assert.Equal(1, handler.Exchanges);
        Assert.Equal("rotated-refresh", (await app.Services.GetRequiredService<ICredentialStore>().GetAsync(key))!.RefreshToken);
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await app.DisposeAsync();
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    private sealed class AtlassianHandler : HttpMessageHandler
    {
        public int Exchanges;
        public Uri? LastApiUri;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host == "auth.atlassian.com")
            {
                Interlocked.Increment(ref Exchanges);
                Assert.Equal("/oauth/token", request.RequestUri.AbsolutePath);
                Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
                var body = JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken))!;
                Assert.Equal("synthetic-client", body["client_id"]!.GetValue<string>());
                Assert.Equal("synthetic-secret", body["client_secret"]!.GetValue<string>());
                Assert.Null(body["code_verifier"]);
                if (body["grant_type"]!.GetValue<string>() == "refresh_token")
                    Assert.Equal("synthetic-refresh", body["refresh_token"]!.GetValue<string>());
                else
                {
                    Assert.Equal("authorization_code", body["grant_type"]!.GetValue<string>());
                    Assert.Equal("synthetic-code", body["code"]!.GetValue<string>());
                }
                return new(HttpStatusCode.OK) { Content = new StringContent("""{"access_token":"synthetic-access","refresh_token":"rotated-refresh","expires_in":3600,"token_type":"bearer"}""") };
            }
            LastApiUri = request.RequestUri;
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("synthetic-access", request.Headers.Authorization.Parameter);
            return new(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }

    private sealed class SampleAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var token = Request.Headers.Authorization.ToString();
            if (token is not ("Bearer admin" or "Bearer runtime")) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim> { new("tenant_id", "sample-tenant") };
            if (token == "Bearer admin") claims.Add(new("connector_admin", "true"));
            return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
        }
    }
}
