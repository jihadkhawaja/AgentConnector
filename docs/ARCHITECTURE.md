# Architecture

AgentConnector is a single .NET library with explicit responsibilities, not a collection of independently deployed services. Keep changes within the owning component and preserve the public contracts used by hosts.

## Responsibilities

| Boundary | Components | Responsibility |
| --- | --- | --- |
| Domain contracts | `Contracts.cs` | Provider and action descriptions, connection identities, credentials, results, errors, and storage/audit interfaces. Connection and credential validation belongs here, independent of file storage and HTTP request construction. |
| Application execution | `ConnectorRuntime` | Enforce action/proxy policy and input limits, build requests, obtain credentials, dispatch requests, interpret provider errors, and audit executions. |
| Connection lifecycle | `ConnectorConnectionService` | Validate management operations, read/write/delete/list credentials through `ICredentialStore`, serialize mutations and refreshes, and enforce required scopes. |
| Authorization flow | `ConnectorOAuth`, `ConnectionCoordinator` | Perform OAuth authorization and token exchange, enforce single-use state and PKCE, and fence stale callbacks after connection changes. |
| Provider adapters | `ProviderCatalog`, `ActionSchemas`, `ProviderRequestBuilder` | Describe provider operations and translate validated inputs and credentials into provider-specific HTTP requests. |
| Infrastructure | `EncryptedFileCredentialStore`, `ConnectorTransport`, `NetworkGuard`, `LoggingConnectorAuditSink` | Encrypt and persist credentials, enforce network restrictions and transport limits, and emit execution audit events. |
| ASP.NET Core integration | `ServiceCollectionExtensions`, `EndpointRouteBuilderExtensions` | Compose services, map HTTP contracts, and enforce host authentication and tenant authorization. |

These are logical boundaries within the existing assembly. There are no additional layer projects, mediator dependencies, or interfaces for every class. Storage and auditing use interfaces because hosts need to replace those implementations.

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