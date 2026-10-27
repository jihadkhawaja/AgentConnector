# AgentConnector

[![NuGet](https://img.shields.io/nuget/v/AgentConnector.svg)](https://www.nuget.org/packages/AgentConnector/)
[![Downloads](https://img.shields.io/nuget/dt/AgentConnector.svg)](https://www.nuget.org/packages/AgentConnector/)
[![Build](https://github.com/jihadkhawaja/agentconnector/actions/workflows/dotnet.yml/badge.svg)](https://github.com/jihadkhawaja/agentconnector/actions/workflows/dotnet.yml)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)

A C# / .NET 10 library for connecting ASP.NET Core applications to external services. Runs in your server process without Docker, container images, or Node.js.

Includes 48 provider adapters, 158 actions, OAuth, encrypted credentials, tenant isolation, and OpenAPI. The library uses only the .NET and ASP.NET Core shared frameworks.

**Status:** Initial implementation. Credentialed integrations need live-account validation before production use.

## Build

Requires .NET SDK 10.0.403 or newer in the .NET 10 feature bands, and .NET/ASP.NET Core runtime 10.0.14 or newer.

```powershell
dotnet restore AgentConnector.slnx --locked-mode
dotnet build AgentConnector.slnx -c Release --no-restore
dotnet test AgentConnector.slnx -c Release --no-build
```

Run the security analyzers, fresh dependency audit, regression tests, and optional Veracode packaging with PowerShell 7:

```powershell
./eng/Test-Security.ps1 -PrepareVeracode
```

See [security scanning](docs/SECURITY-SCANNING.md) for report locations, SonarQube setup, and checks that require external services.

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
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project samples/AgentConnector.Server --urls http://127.0.0.1:5188
```

The API is at `http://127.0.0.1:5188/connector`. Send the appropriate token in the `Authorization: Bearer <token>` header. The sample uses one fixed tenant; use your application's identity system for production.

Outside Development, the sample rejects HTTP requests and enables HSTS. Configure HTTPS on the server. TLS-terminating proxies require host-managed forwarded-header configuration restricted to trusted proxies; the sample does not trust forwarded headers automatically.

## Documentation

- [Architecture and extension boundaries](docs/ARCHITECTURE.md)
- [Providers and supported operations](docs/PROVIDERS.md)
- [Security and deployment](SECURITY.md)
- [Repository agent guidance](AGENTS.md) and [security scanning agent](.codex/agents/security_scanner.toml)
- [Features and limitations](docs/COMPATIBILITY.md)

## License

[Apache 2.0](LICENSE).
