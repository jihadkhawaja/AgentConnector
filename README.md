# AgentConnector

[![NuGet](https://img.shields.io/nuget/v/AgentConnector.svg)](https://www.nuget.org/packages/AgentConnector/)
[![Downloads](https://img.shields.io/nuget/dt/AgentConnector.svg)](https://www.nuget.org/packages/AgentConnector/)
[![Build & security](https://img.shields.io/github/actions/workflow/status/jihadkhawaja/agentconnector/dotnet.yml?label=build%20%26%20security)](https://github.com/jihadkhawaja/agentconnector/actions/workflows/dotnet.yml)
[![CodeQL](https://img.shields.io/github/actions/workflow/status/jihadkhawaja/agentconnector/codeql.yml?label=CodeQL)](https://github.com/jihadkhawaja/agentconnector/actions/workflows/codeql.yml)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)

**Connect your ASP.NET Core app to the services your agents use.**

AgentConnector embeds provider actions, OAuth, and credential management in your .NET application. It runs in your server process and uses only the .NET and ASP.NET Core shared frameworks.

[Get started](#get-started) · [Credential storage](#credential-storage) · [Try the sample](#try-the-sample) · [Providers](docs/PROVIDERS.md) · [Documentation](#documentation)

| | Included |
| --- | --- |
| **Integrations** | 48 provider adapters and 158 named actions, including GitHub, Slack, Google Workspace, Microsoft Graph, and Atlassian. |
| **Connections** | OAuth authorization, token refresh, and encrypted credentials scoped to tenants and connection names. |
| **Access control** | Explicit action grants, separate runtime and admin policies, and proxies denied by default. |
| **Discovery** | Provider and action endpoints, JSON input schemas, and an OpenAPI document. |
| **Hosting** | An embeddable .NET 10 library with no third-party runtime package dependencies. |

> **Project status:** Initial implementation. Credentialed integrations need live-account validation before production use. See [provider coverage and limits](docs/PROVIDERS.md).

## Get started

Use an ASP.NET Core host targeting .NET 10. The repository requires SDK **10.0.403 or newer within .NET 10** and .NET/ASP.NET Core runtime **10.0.14 or newer**; [global.json](global.json) and [Directory.Build.props](Directory.Build.props) define the supported versions.

### 1. Add the package

Run from your host project's directory:

```shell
dotnet add package AgentConnector
```

When working from this repository, you can reference [the library project](src/AgentConnector/AgentConnector.csproj) directly instead.

### 2. Register the connector

In `Program.cs`, add the namespace and register the actions your application needs before `builder.Build()`. Choose your [credential store](#credential-storage) as part of this setup; registration uses the encrypted file store by default.

```csharp
using AgentConnector;

builder.Services.AddAgentConnector(options =>
{
    options.AllowedActions.Add("github.get_current_user");
});
```

### 3. Map the endpoints

In the same host, map the connector after your authentication and authorization middleware:

```csharp
var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapAgentConnector("/connector");

app.Run();
```

These snippets extend an existing host. Configure its authentication scheme and credential storage before storing real credentials. The default file store requires persistent, protected Data Protection keys. Authenticated callers must have exactly one trusted `tenant_id` claim; management endpoints also require `connector_admin=true` under the default policies.

Only explicitly granted actions can run. The GitHub action above also needs a stored GitHub connection. See [security and deployment](SECURITY.md) for host requirements, key protection, and the default store's single-instance coordination limits, or use the [sample below](#try-the-sample) for a complete host.

## Credential storage

AgentConnector needs to retrieve provider credentials when executing an action and save updated tokens after an OAuth refresh. These are the connected provider's API keys, access tokens, refresh tokens, or basic-auth credentials. Your host's authentication credentials and OAuth app client secrets are configured separately.

Storage is defined by [`ICredentialStore`](src/AgentConnector/Application/Abstractions/ICredentialStore.cs). The library includes an encrypted file implementation; your application can supply an implementation backed by its database or a secret manager such as HashiCorp Vault or Azure Key Vault. The [Vault and encrypted SQLite sample](samples/AgentConnector.StorageSample/README.md) includes runnable adapters and setup instructions. These adapters live in the sample, rather than the library package; an Azure Key Vault adapter is not included.

### What does `CredentialDirectory` mean?

This option selects the filesystem location used by the default `EncryptedFileCredentialStore`:

```csharp
builder.Services.AddAgentConnector(options =>
{
    // Only used by the built-in file store; this is an example Linux path.
    options.CredentialDirectory = "/var/lib/myapp/credentials";
    options.AllowedActions.Add("github.get_current_user");
});
```

The store writes encrypted `.credential` files under hashed tenant directories, with a separate record for each provider and connection name. ASP.NET Core Data Protection encrypts and authenticates each record and binds it to its tenant and connection identity. The directory contains credential records; the Data Protection key ring is configured separately.

If omitted, the directory defaults to `.data/credentials` under `AppContext.BaseDirectory`. For real credentials, explicitly choose a persistent service directory outside the repository, web root, and synchronized folders, restrict filesystem permissions, and configure a persistent, protected key ring. Losing the keys makes the stored credentials unreadable. See [key custody](SECURITY.md#encryption-key-custody) for configuration examples.

### Use your database or vault

For complete examples, run the [storage sample host](samples/AgentConnector.StorageSample/README.md) with `Connector__CredentialStore` set to `Vault` or `Sqlite`. Both stores support saved connections and OAuth credential updates. The guide includes a Vault policy and a synthetic-data smoke test.

Register your implementation **before** `AddAgentConnector`. Its default store registration uses `TryAddSingleton`, so your registration takes precedence:

```csharp
using AgentConnector;

// MyCredentialStore is a class you implement in your host application.
builder.Services.AddSingleton<ICredentialStore, MyCredentialStore>();

builder.Services.AddAgentConnector(options =>
{
    options.AllowedActions.Add("github.get_current_user");
});
```

With a custom store, you do not need to set `CredentialDirectory`; the connector's file store is not used. A custom store owns its encryption and key-management strategy. AgentConnector does not automatically apply the file store's Data Protection encryption to other implementations.

| Store operation | Implementation responsibility |
| --- | --- |
| `GetAsync` | Retrieve a credential by the complete `ConnectionKey`: tenant ID, provider ID, and connection name. Return `null` when absent. |
| `SetAsync` | Save or replace the credential, including refreshed access tokens, refresh tokens, expiry, and scopes. |
| `DeleteAsync` | Remove the selected connection's credential record. This does not revoke tokens at the provider. |
| `ListAsync` | Return `ConnectionInfo` metadata for the requested tenant without exposing secrets. |

For a database implementation, preserve tenant isolation in every query and protect credential payloads at rest, with keys managed according to your deployment's requirements. For a vault implementation, map the complete connection identity to a secret and provide tenant-scoped metadata listing, either through the vault or a separate index. Both implementations must support updates: OAuth callbacks and refreshes persist new credentials through `SetAsync`; a read-only secret lookup does not cover the full lifecycle.

Stores are consumed by singleton services. Implementations must be safe for concurrent use and honor cancellation; database implementations should create a scope or use a context factory rather than capture a scoped database context in the singleton.

**Shared storage does not provide distributed coordination.** OAuth state, connection locks, and refresh coordination remain process-local even when the credentials are in a shared database or vault. Multi-instance deployments need additional distributed state and locking with durable fencing. See [OAuth and concurrency](SECURITY.md#oauth-and-concurrency) and [extension rules](docs/ARCHITECTURE.md#extension-rules).

## Try the sample

The [sample server](samples/AgentConnector.Server/Program.cs) includes authentication and allows Hacker News reads without provider credentials. From the repository root, use PowerShell 7 to generate separate runtime and admin tokens and start the server:

```powershell
$env:Connector__RuntimeToken = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$env:Connector__AdminToken = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$env:ASPNETCORE_ENVIRONMENT = "Development"

dotnet run --project samples/AgentConnector.Server --urls http://127.0.0.1:5188
```

From another PowerShell terminal, set `$env:Connector__RuntimeToken` to the same runtime token used by the server, then request the top Hacker News story IDs:

```powershell
Invoke-RestMethod `
    -Uri "http://127.0.0.1:5188/connector/v1/actions/hackernews.get_top_stories" `
    -Method Post `
    -Headers @{ Authorization = "Bearer $env:Connector__RuntimeToken" } `
    -ContentType "application/json" `
    -Body '{"input":{}}'
```

The response wraps the provider's JSON in `data` and includes execution metadata in `meta`. Hacker News does not require a saved connection; credentialed providers do.

The sample uses one fixed tenant and development bearer tokens. Use your application's identity system for production. Store tokens in environment variables or a secret manager.

Outside Development, the sample rejects HTTP requests and enables HSTS. Configure HTTPS on the server. TLS-terminating proxies require host-managed forwarded-header configuration restricted to trusted proxies; the sample does not trust forwarded headers automatically.

### Jira and Confluence OAuth

See the [Atlassian sample guide](samples/AgentConnector.Server/ATLASSIAN.md) for developer-console app registration, minimal Jira/Confluence permissions, and configuration. The sample includes a browser sign-in page that opens Atlassian OAuth in a popup, handles the callback, and keeps provider tokens on the server, plus runnable agent read examples.

## API at a glance

Routes below use the default `/connector` prefix. Runtime and admin routes require an authenticated tenant identity and the corresponding host policy.

| Method | Route | Purpose | Access |
| --- | --- | --- | --- |
| `GET` | `/connector/v1/providers` | List providers and configuration status. | Runtime |
| `GET` | `/connector/v1/actions` | Discover allowed actions and input schemas. | Runtime |
| `POST` | `/connector/v1/actions/{actionId}` | Execute an allowed action. | Runtime |
| `GET` | `/connector/v1/connections` | List connection metadata for the current tenant. | Runtime |
| `PUT` / `DELETE` | `/connector/api/connections/{providerId}` | Save or remove a connection. | Admin |
| `POST` | `/connector/api/oauth/{providerId}/authorize` | Begin OAuth authorization. | Admin |
| `GET` | `/connector/openapi.json` | Read the generated OpenAPI document. | Runtime |

Actions return provider-native JSON. Pagination is caller-managed; streaming, binary transfers, automatic retries, and durable idempotency are not implemented. See [features and limitations](docs/COMPATIBILITY.md).

## Build and verify

From the repository root:

```powershell
dotnet restore AgentConnector.slnx --locked-mode
dotnet build AgentConnector.slnx -c Release --no-restore
dotnet test AgentConnector.slnx -c Release --no-build --no-restore
```

Run the security analyzers, fresh dependency audit, regression tests, and optional Veracode packaging with PowerShell 7:

```powershell
./eng/Test-Security.ps1 -PrepareVeracode
```

See [security scanning](docs/SECURITY-SCANNING.md) for report locations, SonarQube setup, and checks that require external services.

The **Build & security** badge tracks the CI build, including analyzers, dependency auditing, and regression tests. **CodeQL** tracks the separate security analysis workflow. Both show workflow status; inspect the linked runs and security findings for details.

## Documentation

| Guide | What you'll find |
| --- | --- |
| [Provider coverage](docs/PROVIDERS.md) | Supported operations, authentication, and instance-specific endpoints. |
| [Architecture](docs/ARCHITECTURE.md) | Component ownership, execution flow, and extension boundaries. |
| [Security and deployment](SECURITY.md) | Trust boundaries, key custody, network controls, and deployment requirements. |
| [Features and limitations](docs/COMPATIBILITY.md) | Supported behavior and integration limits. |
| [Atlassian sample](samples/AgentConnector.Server/ATLASSIAN.md) | Jira and Confluence OAuth setup and runnable examples. |
| [Vault and database sample](samples/AgentConnector.StorageSample/README.md) | HashiCorp Vault KV v2 and encrypted SQLite credential stores, configuration, and smoke testing. |
| [Security scanning](docs/SECURITY-SCANNING.md) | Local gates and external scanner setup. |
| [Contributing](CONTRIBUTING.md) | Development setup, verification, and pull request guidance. |
| [Code of conduct](CODE_OF_CONDUCT.md) | Community expectations, reporting, and enforcement. |
| [Accessibility](ACCESSIBILITY.md) | Accessibility goals, known limitations, and reporting barriers. |
| [Repository agent guidance](AGENTS.md) | Contribution rules and the [security scanning agent](.codex/agents/security_scanner.toml). |

## License

[Apache 2.0](LICENSE).
