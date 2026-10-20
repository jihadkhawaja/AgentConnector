# AgentConnector

A C# / .NET 10 library for connecting ASP.NET Core applications to external services. Runs in your server process without Docker, container images, or Node.js.

Includes 48 provider adapters, 158 actions, OAuth, encrypted credentials, tenant isolation, and OpenAPI. The library uses only the .NET and ASP.NET Core shared frameworks.

**Status:** Initial implementation. Credentialed integrations need live-account validation before production use.

## Build

Requires the .NET 10 SDK.

```powershell
dotnet restore AgentConnector.slnx --locked-mode
dotnet build AgentConnector.slnx -c Release --no-restore
dotnet test AgentConnector.slnx -c Release --no-build
```

## Use In ASP.NET Core

Reference the library project, then register and map it in your application:

```csharp
using AgentConnector;

builder.Services.AddAgentConnector(options =>
{
    options.CredentialDirectory = "/var/lib/myapp/credentials";
    options.AllowedActions.Add("github.get_current_user");
});

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapAgentConnector("/connector");
app.Run();
```

Your host must configure authentication and provide a trusted `tenant_id` claim. Management endpoints also require `connector_admin=true`. Actions and proxies are denied unless explicitly allowed. Configure persistent, protected Data Protection keys before storing real credentials.

## Run The Sample

Set `Connector__RuntimeToken` and `Connector__AdminToken` to distinct random values of at least 32 characters, then run:

```powershell
dotnet run --project samples/AgentConnector.Server --urls http://127.0.0.1:5188
```

The API is at `http://127.0.0.1:5188/connector`. Send the appropriate token in the `Authorization: Bearer <token>` header. The sample uses one fixed tenant; use your application's identity system for production.

## Documentation

- [Providers and supported operations](docs/PROVIDERS.md)
- [Security and deployment](SECURITY.md)
- [Features and limitations](docs/COMPATIBILITY.md)

## License

[Apache 2.0](LICENSE).