using AgentConnector;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace AgentConnector.Sample;

public static class AtlassianSample
{
    public static bool Configure(ConnectorOptions options, IConfiguration configuration)
    {
        var settings = configuration.GetSection("Atlassian");
        if (!settings.Exists()) return false;

        var clientId = Required(settings, "ClientId");
        var clientSecret = Required(settings, "ClientSecret");
        if (!Guid.TryParseExact(Required(settings, "CloudId"), "D", out var cloudId))
            throw new InvalidOperationException("Atlassian:CloudId must be the site's cloud ID (a UUID).");
        if (!Uri.TryCreate(Required(settings, "PublicOrigin"), UriKind.Absolute, out var origin) ||
            (origin.Scheme != "https" && !(origin.Scheme == "http" && origin.IsLoopback)) ||
            origin.UserInfo.Length != 0 || origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0)
            throw new InvalidOperationException("Atlassian:PublicOrigin must be an HTTPS origin, or loopback HTTP for development, without a path, query, or fragment.");

        // Both connections use the full union: a later consent for the same app can replace its earlier grant.
        string[] scopes = ["read:jira-user", "read:jira-work", "read:space:confluence", "read:page:confluence", "offline_access"];
        var endpoints = new OAuthEndpoints(new("https://auth.atlassian.com/authorize"), new("https://auth.atlassian.com/oauth/token"),
            UsePkce: false, UseJsonTokenBody: true,
            AuthorizationParameters: new Dictionary<string, string>
            {
                ["audience"] = "api.atlassian.com", ["prompt"] = "consent"
            });
        foreach (var provider in new[] { "jira", "confluence" })
        {
            options.ProviderBaseUris[provider] = new($"https://api.atlassian.com/ex/{provider}/{cloudId:D}/");
            options.OAuthClients[provider] = new()
            {
                ClientId = clientId, ClientSecret = clientSecret,
                RedirectUri = new(origin, "/atlassian/oauth/callback"), Scopes = scopes, Endpoints = endpoints
            };
        }
        options.AllowedActions.UnionWith([
            "jira.get_current_user", "jira.list_projects", "jira.get_issue",
            "confluence.list_spaces", "confluence.get_page"
        ]);
        return true;
    }

    public static void MapAtlassianSample(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/atlassian"))
            {
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                context.Response.Headers.XContentTypeOptions = "nosniff";
                context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'";
                context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin-allow-popups";
            }
            await next(context);
        });
        app.UseStaticFiles();
        app.MapGet("/atlassian", () => Results.Redirect("/atlassian/index.html"));
        app.MapGet("/atlassian/oauth/callback", async (HttpContext context, ConnectorOAuth oauth) =>
        {
            // Preserve the opener when returning from Atlassian's cross-origin document.
            context.Response.Headers["Cross-Origin-Opener-Policy"] = "unsafe-none";
            try
            {
                await oauth.CompleteAsync(context.Request.Query["state"].ToString(), context.Request.Query["code"].ToString(),
                    context.Request.Query.ContainsKey("error") ? "denied" : null, context.RequestAborted);
                return Callback(true, StatusCodes.Status200OK);
            }
            catch (ConnectorException)
            {
                return Callback(false, StatusCodes.Status400BadRequest);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
            catch
            {
                return Callback(false, StatusCodes.Status503ServiceUnavailable);
            }
        }).AllowAnonymous();
    }

    private static IResult Callback(bool success, int statusCode) => Results.Content($$"""
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8"><title>Atlassian connection</title>
        <script src="/atlassian/callback.js" defer></script></head>
        <body data-success="{{(success ? "true" : "false")}}"><p>{{(success
            ? "Authorization completed. You can close this window and check connections in the sample."
            : "Authorization failed or expired. Close this window and try signing in again.")}}</p></body></html>
        """, "text/html; charset=utf-8", statusCode: statusCode);

    private static string Required(IConfiguration settings, string name) =>
        !string.IsNullOrWhiteSpace(settings[name]) ? settings[name]! :
            throw new InvalidOperationException($"Set Atlassian:{name} in server configuration. See samples/AgentConnector.Server/ATLASSIAN.md.");
}
