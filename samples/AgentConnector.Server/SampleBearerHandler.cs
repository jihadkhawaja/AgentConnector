using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AgentConnector.Sample;

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
