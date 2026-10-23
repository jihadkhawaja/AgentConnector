# Architecture

AgentConnector uses architectural folders within a single .NET library. Domain models, application services and ports, infrastructure adapters, HTTP presentation, and dependency injection each have a dedicated home. Keep changes within the owning component and preserve the public contracts used by hosts.

## Folder Structure

```text
src/AgentConnector/
    AgentConnector.csproj
    Domain/
        Connections/
            ConnectionKey.cs
            ConnectorCredential.cs
            CredentialKind.cs
        Exceptions/
            ConnectorException.cs
        Providers/
            ActionParameter.cs
            ConnectorAction.cs
            ConnectorProvider.cs
            OAuthEndpoints.cs
            ParameterKind.cs
            ParameterLocation.cs
    Application/
        Abstractions/
            IConnectorAuditSink.cs
            ICredentialStore.cs
        Configuration/
            ConnectorOptions.cs
            OAuthClientOptions.cs
        Connections/
            ConnectorConnectionService.cs
        Contracts/
            ActionResult.cs
            ConnectionInfo.cs
            ExecutionAudit.cs
            ProxyRequest.cs
        Execution/
            ConnectorRuntime.cs
        Policies/
            ConnectorPolicy.cs
    Infrastructure/
        Auditing/
            LoggingConnectorAuditSink.cs
        Concurrency/
            ConnectionCoordinator.cs
        Http/
            ConnectorHttpResponse.cs
            ConnectorTransport.cs
            NetworkGuard.cs
        OAuth/
            ConnectorOAuth.cs
            OAuthAuthorization.cs
        Persistence/
            EncryptedFileCredentialStore.cs
        Providers/
            ProviderCatalog.cs
            ProviderRequestBuilder.cs
    Presentation/
        AspNetCore/
            ActionSchemas.cs
            EndpointRouteBuilderExtensions.cs
    DependencyInjection/
        ServiceCollectionExtensions.cs
```

Public types keep the `AgentConnector` namespace for source and binary compatibility. Folders describe ownership without changing consumers' imports. Each public type has its own source file; storage/audit ports belong to `Application/Abstractions`, not to the domain model or their infrastructure implementations.

The tests mirror these folders under `tests/AgentConnector.Tests/Application`, `Infrastructure`, and `Presentation`. End-to-end OAuth/runtime tests stay together under `Application/Execution`, and combined storage/network tests stay under `Infrastructure`.

## Responsibilities

| Boundary | Components | Responsibility |
| --- | --- | --- |
| Domain | `Domain/Connections`, `Domain/Providers`, `Domain/Exceptions` | Provider/action models, connection identities, credentials, validation rules, and errors. Domain code does not depend on application services, infrastructure adapters, or ASP.NET Core. |
| Application contracts and ports | `Application/Contracts`, `Application/Abstractions` | Execution and connection DTOs, execution audit records, and the storage/audit interfaces used by application services. |
| Application configuration and policy | `Application/Configuration`, `Application/Policies` | Connector limits, OAuth client configuration, and deny-by-default action/proxy authorization rules. |
| Application execution | `ConnectorRuntime` | Enforce action/proxy policy and input limits, build requests, obtain credentials, dispatch requests, interpret provider errors, and audit executions. |
| Connection lifecycle | `ConnectorConnectionService` | Validate management operations, read/write/delete/list credentials through `ICredentialStore`, serialize mutations and refreshes, and enforce required scopes. |
| Authorization infrastructure | `Infrastructure/OAuth`, `Infrastructure/Concurrency` | Perform OAuth authorization and token exchange, enforce single-use state and PKCE, and fence stale callbacks after connection changes. |
| Provider adapters | `Infrastructure/Providers` | Describe provider operations and translate validated inputs and credentials into provider-specific HTTP requests. |
| Infrastructure | `EncryptedFileCredentialStore`, `ConnectorTransport`, `NetworkGuard`, `LoggingConnectorAuditSink` | Encrypt and persist credentials, enforce network restrictions and transport limits, and emit execution audit events. |
| ASP.NET Core presentation | `Presentation/AspNetCore` | Map HTTP contracts, describe JSON/OpenAPI schemas, and enforce host authentication and tenant authorization. |
| Composition root | `DependencyInjection/ServiceCollectionExtensions.cs` | Register application services, infrastructure implementations, authorization defaults, and HTTP clients. |

Folders make responsibilities visible but do not enforce dependency direction at compile time. The library still has application services that consume concrete provider, OAuth, coordination, and transport components; this organization does not claim strict Clean Architecture isolation. Separate assemblies and additional ports would be a further dependency-inversion refactor, not a prerequisite for this compatible folder reorganization. Storage and auditing already use interfaces because hosts need to replace those implementations.

## Execution Flow

1. The HTTP endpoint authenticates the caller and obtains its trusted tenant identity.
2. `ConnectorRuntime` resolves the operation and checks policy, provider configuration, connection identity, and input limits.
3. `ProviderRequestBuilder` constructs an origin-constrained request.
4. `ConnectorConnectionService` loads the selected credential, refreshes it if necessary under the shared connection lock, and checks scopes. Credential-free providers skip storage.
5. The request builder applies authentication and `ConnectorTransport` dispatches the request through the network guard.
6. The runtime interprets application-level provider errors and writes the execution audit, even when credential resolution or dispatch fails.

The runtime disposes requests it builds. The HTTP client factory owns transport handlers. Cancellation tokens flow through storage, locking, OAuth, and transport operations; execution auditing deliberately uses an independent token so request cancellation does not suppress the audit attempt.

## Connection Management

Hosts can inject `ConnectorConnectionService` directly when only connection management is needed:

```csharp
public sealed class ConnectionAdministration(ConnectorConnectionService connections)
{
    public Task DisconnectAsync(ConnectionKey key, CancellationToken cancellationToken) =>
        connections.DeleteAsync(key, cancellationToken);
}
```

The host must authenticate and authorize callers and construct `ConnectionKey` from a trusted tenant identity. The application service is not an authorization boundary.

`SetAsync` and `DeleteAsync` share the same `ConnectionCoordinator` as OAuth and execution. Mutations invalidate pending OAuth state; concurrent executions refresh a connection only once. Coordination remains process-local: multi-instance hosts need a distributed coordination design before sharing mutable credentials.

`ConnectorRuntime.SetConnectionAsync`, `DeleteConnectionAsync`, and `ListConnectionsAsync` remain compatibility facades. Its original constructor is also retained. Dependency injection explicitly constructs the runtime with the registered connection service to avoid ambiguity between constructors.

## Extension Rules

- Register a custom `ICredentialStore` or `IConnectorAuditSink` before calling `AddAgentConnector`; the defaults use `TryAddSingleton`. Implementations must be safe for concurrent use because application services are singletons.
- A custom store must isolate tenants and connection names, honor cancellation, and protect secrets at rest. Domain validation is not delegated to a specific storage implementation.
- Use `ConnectionKey.Validate()` and `ConnectorCredential.Validate(provider)` for domain validation. The public `EncryptedFileCredentialStore.ValidateKey` and `ProviderRequestBuilder.ValidateCredential` methods remain forwarding wrappers for existing consumers.
- Keep provider HTTP details in the adapter/request-builder boundary, not in storage or endpoint handlers.
- Keep authentication and management authorization in the host/endpoint boundary. Direct runtime and connection-service callers must enforce their own authorization.
- Preserve deny-by-default action/proxy policy, origin restrictions, tenant isolation, secret redaction, and OAuth invalidation when refactoring.

## Verification

Tests mirror behavior rather than implementation layers: request construction and validation, encrypted storage and network restrictions, OAuth and runtime coordination, and ASP.NET integration. Run the repository gates from the root:

```powershell
dotnet restore AgentConnector.slnx --locked-mode
dotnet build AgentConnector.slnx -c Release --no-restore
dotnet test AgentConnector.slnx -c Release --no-build
dotnet pack src/AgentConnector -c Release --no-build -o artifacts/packages
dotnet publish samples/AgentConnector.Server -c Release --no-build -o artifacts/server
```