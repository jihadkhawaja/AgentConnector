using AgentConnector;
using AgentConnector.Sample;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;

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
var atlassianEnabled = false;
builder.Services.AddAgentConnector(options =>
{
    options.CredentialDirectory = Path.Combine(dataDirectory, "credentials");
    options.AllowedActions.UnionWith(["hackernews.*", "github.get_current_user", "github.list_repositories"]);
    atlassianEnabled = AtlassianSample.Configure(options, builder.Configuration);
});
builder.Services.AddAuthorization();
var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.Use(async (context, next) =>
    {
        if (!context.Request.IsHttps)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        await next(context);
    });
}
app.UseAuthentication();
app.UseAuthorization();
if (atlassianEnabled) app.MapAtlassianSample();
app.MapAgentConnector();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
await app.RunAsync();
