using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using AgentConnector;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentConnector.Tests;

public sealed class AspNetIntegrationTests : IAsyncLifetime
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "connector-http-" + Guid.NewGuid().ToString("N"));
    private WebApplication app = null!;
    private HttpClient client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("test", _ => { });
        builder.Services.AddAgentConnector(options =>
        {
            options.CredentialDirectory = directory;
            options.MaximumRequestBytes = 1024;
            options.AllowedActions.Add("github.*");
        });
        app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapAgentConnector();
        await app.StartAsync();
        client = app.GetTestClient();
    }

    [Fact]
    public async Task RequiresAuthenticationAdminPolicyAndTenantClaim()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/connector/v1/providers")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "runtime");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/connector/v1/providers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/connector/api/connections")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "no-tenant");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/connector/v1/providers")).StatusCode);
    }

    [Fact]
    public async Task ConnectionManagementNeverReturnsStoredSecretsAndIsTenantScoped()
    {
        client.DefaultRequestHeaders.Authorization = new("Bearer", "admin");
        var saved = await client.PutAsync("/connector/api/connections/github", Json("""{"credential":{"kind":"bearer","secret":"sensitive-test-secret"},"connectionName":"work"}"""));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.DoesNotContain("sensitive-test-secret", await saved.Content.ReadAsStringAsync());
        var listed = await client.GetStringAsync("/connector/api/connections");
        Assert.Contains("work", listed);
        Assert.DoesNotContain("sensitive-test-secret", listed);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "other-tenant");
        var other = JsonNode.Parse(await client.GetStringAsync("/connector/v1/connections"))!;
        Assert.Empty(other["data"]!.AsArray());
    }

    [Fact]
    public async Task RejectsOversizedBodiesUnknownPropertiesAndConflictingAliases()
    {
        client.DefaultRequestHeaders.Authorization = new("Bearer", "runtime");
        var oversized = await client.PostAsync("/connector/v1/actions/github.get_current_user", Json("{\"input\":{\"padding\":\"" + new string('x', 2000) + "\"}}"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        var unknown = await client.PostAsync("/connector/v1/actions/github.get_current_user", Json("""{"input":{},"tenantId":"attacker"}"""));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        var conflicting = await client.PostAsync("/connector/v1/actions/github.get_current_user?alias=personal", Json("""{"input":{},"connectionName":"work"}"""));
        Assert.Equal(HttpStatusCode.BadRequest, conflicting.StatusCode);
        var proxy = await client.PostAsync("/connector/v1/proxy/github", Json("""{"endpoint":"/user"}"""));
        Assert.Equal(HttpStatusCode.Forbidden, proxy.StatusCode);
    }

    [Fact]
    public async Task OAuthCredentialWireFormatUsesExplicitNamesAndNeverEchoesTokens()
    {
        client.DefaultRequestHeaders.Authorization = new("Bearer", "admin");
        var response = await client.PutAsync("/connector/api/connections/github", Json("""{"credential":{"kind":"oauth2","secret":"access-secret","refreshToken":"refresh-secret"}}"""));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Equal("oauth2", JsonNode.Parse(json)!["data"]!["kind"]!.GetValue<string>());
        Assert.DoesNotContain("access-secret", json);
        Assert.DoesNotContain("refresh-secret", json);
    }

    [Fact]
    public async Task RejectsUnsupportedSafetyHeadersRatherThanIgnoringThem()
    {
        client.DefaultRequestHeaders.Authorization = new("Bearer", "runtime");
        client.DefaultRequestHeaders.Add("Idempotency-Key", "test-key");
        var idempotent = await client.PostAsync("/connector/v1/actions/github.get_current_user", Json("""{"input":{}}"""));
        Assert.Equal(HttpStatusCode.BadRequest, idempotent.StatusCode);
        Assert.Contains("unsupported_feature", await idempotent.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("x-oo-connector-app-id", "unknown-stable-id");
        var selected = await client.PostAsync("/connector/v1/actions/github.get_current_user", Json("""{"input":{}}"""));
        Assert.Equal(HttpStatusCode.BadRequest, selected.StatusCode);
        Assert.Contains("unsupported_selector", await selected.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ExposesPolicyFilteredSchemasAndValidOpenApi()
    {
        client.DefaultRequestHeaders.Authorization = new("Bearer", "runtime");
        var action = JsonNode.Parse(await client.GetStringAsync("/connector/v1/actions/github.get_current_user"))!;
        Assert.Equal("object", action["data"]!["inputSchema"]!["type"]!.GetValue<string>());
        var document = JsonNode.Parse(await client.GetStringAsync("/connector/openapi.json"))!;
        Assert.Equal("3.1.0", document["openapi"]!.GetValue<string>());
        Assert.NotNull(document["paths"]!["/v1/actions/github.get_current_user"]);
        Assert.Null(document["paths"]!["/v1/actions/stripe.list_customers"]);
        var callback = await client.GetAsync("/connector/oauth/callback?state=invalid&code=test");
        Assert.Equal(HttpStatusCode.BadRequest, callback.StatusCode);
        Assert.True(callback.Headers.CacheControl!.NoStore);
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await app.DisposeAsync();
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    private static StringContent Json(string value) => new(value, Encoding.UTF8, "application/json");

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var token = Request.Headers.Authorization.ToString();
            if (!token.StartsWith("Bearer ", StringComparison.Ordinal)) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim>();
            if (token != "Bearer no-tenant") claims.Add(new("tenant_id", token == "Bearer other-tenant" ? "other" : "tenant"));
            if (token == "Bearer admin") claims.Add(new("connector_admin", "true"));
            return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
        }
    }
}