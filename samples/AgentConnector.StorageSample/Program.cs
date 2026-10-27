using System.Security.Cryptography.X509Certificates;
using AgentConnector;
using AgentConnector.Sample;
using AgentConnector.StorageSample;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
var runtimeToken = builder.Configuration["Connector:RuntimeToken"];
var adminToken = builder.Configuration["Connector:AdminToken"];
if (runtimeToken is null || adminToken is null || runtimeToken.Length < 32 || adminToken.Length < 32 || runtimeToken == adminToken)
    throw new InvalidOperationException("Set distinct Connector__RuntimeToken and Connector__AdminToken values of at least 32 characters.");

var store = builder.Configuration["Connector:CredentialStore"] ?? "Sqlite";
if (store == "Sqlite")
{
    var configuredDirectory = builder.Configuration["Connector:DataDirectory"];
    if (!builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(configuredDirectory))
        throw new InvalidOperationException("Configure a protected, non-synchronized Connector__DataDirectory outside Development.");
    var directory = Path.GetFullPath(configuredDirectory ?? ".data/storage-sample");
    Directory.CreateDirectory(directory);
    var protection = builder.Services.AddDataProtection().SetApplicationName("AgentConnector.StorageSample")
        .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(directory, "keys")));
    if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
    else if (builder.Configuration["Connector:KeyProtectionCertificatePath"] is { Length: > 0 } certificatePath)
        protection.ProtectKeysWithCertificate(X509CertificateLoader.LoadPkcs12FromFile(certificatePath,
            builder.Configuration["Connector:KeyProtectionCertificatePassword"]));
    else if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("Configure a key-protection certificate outside Development on this platform.");
    builder.Services.AddSingleton(provider => new SqliteCredentialStore(Path.Combine(directory, "credentials.db"),
        provider.GetRequiredService<IDataProtectionProvider>()));
    builder.Services.AddSingleton<ICredentialStore>(provider => provider.GetRequiredService<SqliteCredentialStore>());
}
else if (store == "Vault")
{
    var options = new VaultStoreOptions(
        new Uri(builder.Configuration["Vault:Address"] ?? throw new InvalidOperationException("Configure Vault__Address.")),
        builder.Configuration["Vault:Mount"] ?? "secret",
        builder.Configuration["Vault:Token"] ?? "",
        builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Vault:AllowDevelopmentLoopbackHttp"));
    options.Validate();
    builder.Services.AddHttpClient(VaultCredentialStore.HttpClientName)
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false
        }).RemoveAllLoggers();
    builder.Services.AddSingleton(options);
    builder.Services.AddSingleton<ICredentialStore, VaultCredentialStore>();
}
else throw new InvalidOperationException("Connector__CredentialStore must be Sqlite or Vault.");

builder.Services.AddAuthentication("SampleBearer")
    .AddScheme<AuthenticationSchemeOptions, SampleBearerHandler>("SampleBearer", _ => { });
builder.Services.AddAgentConnector(options => options.AllowedActions.Add("github.get_current_user"));
var app = builder.Build();
if (store == "Sqlite") await app.Services.GetRequiredService<SqliteCredentialStore>().InitializeAsync();
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
app.MapAgentConnector();
await app.RunAsync();
