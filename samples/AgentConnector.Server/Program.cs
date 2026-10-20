using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using AgentConnector;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
var runtimeToken = builder.Configuration["Connector:RuntimeToken"];
var adminToken = builder.Configuration["Connector:AdminToken"];
if (runtimeToken?.Length < 32 || adminToken?.Length < 32 || runtimeToken is null || adminToken is null || runtimeToken == adminToken)
    throw new InvalidOperationException("Set distinct Connector__RuntimeToken and Connector__AdminToken values of at least 32 characters using environment variables or a secret manager.");
var dataDirectory = Path.GetFullPath(builder.Configuration["Connector:DataDirectory"] ?? ".data");
var protection = builder.Services.AddDataProtection().SetApplicationName("AgentConnector.Sample")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys")));
if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();

builder.Services.AddAuthentication("SampleBearer").AddScheme<AuthenticationSchemeOptions, SampleBearerHandler>("SampleBearer", _ => { });
builder.Services.AddAgentConnector(options =>
{
    options.CredentialDirectory = Path.Combine(dataDirectory, "credentials");
    options.AllowedActions.UnionWith(["hackernews.*", "github.get_current_user", "github.list_repositories"]);
});
builder.Services.AddAuthorization();
var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapAgentConnector();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.Run();

public sealed class SampleBearerHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, IConfiguration configuration) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal)) return Task.FromResult(AuthenticateResult.NoResult());
        var value = header[7..];
        var admin = Matches(value, configuration["Connector:AdminToken"]);
        if (!admin && !Matches(value, configuration["Connector:RuntimeToken"]))
            return Task.FromResult(AuthenticateResult.Fail("Invalid token."));
        var claims = new List<Claim> { new("tenant_id", "sample-tenant"), new(ClaimTypes.NameIdentifier, admin ? "sample-admin" : "sample-runtime") };
        if (admin) claims.Add(new("connector_admin", "true"));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new(principal, Scheme.Name)));
    }

    private static bool Matches(string provided, string? expected) => !string.IsNullOrEmpty(expected) &&
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(provided)), SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}