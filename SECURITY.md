# Security And Deployment Requirements

Assess security through code behavior, dependencies, permissions, key custody, and data paths. This implementation runs locally without a hosted connector dependency; review your deployment before production use.

## Trust Boundaries

- The host application, registered provider definitions, options, credential store, audit sink, and .NET runtime are trusted. Do not load these from untrusted tenants or remote catalogs.
- HTTP identities must be authenticated by the host. Do not mint tenant/admin claims from caller-provided fields. Every tenant member authorized by the runtime policy can use the tenant's granted connections by default; add host-level resource authorization for narrower access.
- Every action and proxy starts denied. Prefer exact action grants. GraphQL and SQL actions can have broad effects despite appearing as one action.
- APIs containing credentials must be served over HTTPS. Avoid permissive CORS. Cookie-authenticated hosts must apply their CSRF protections to unsafe routes; the JSON API's content-type checks do not replace host security policy.
- Limit ingress rates and concurrent requests in your host/reverse proxy. Configure header/body read timeouts, request body limits, cancellation, and deployment-specific egress firewall rules. The library's bounded reads do not replace slow-client protection.

## Encryption Key Custody

Credentials are encrypted and authenticated with ASP.NET Core Data Protection, purpose-bound to tenant and connection record. File names are hashes of structured identities, not user-supplied paths. Records are written via atomic file replacement. **Encryption is only as safe as the key ring.**

Configure persistent keys before real credentials are stored, isolate the service account, restrict directory permissions, back up keys securely, and choose a stable Data Protection application name. Do not use ephemeral keys in production. Losing the key ring makes existing credentials unreadable. Do not share a key ring between unrelated applications.

On Windows, the sample persists keys and protects them with DPAPI for its service identity. A service identity change requires a migration plan. On Linux, the default filesystem key ring is not automatically encrypted; protect it with an application-managed certificate (with a protected private key) or your platform's supported key-management integration:

```csharp
builder.Services.AddDataProtection()
    .SetApplicationName("YourProduct.Connector")
    .PersistKeysToFileSystem(new DirectoryInfo("/var/lib/yourproduct/connector-keys"))
    .ProtectKeysWithCertificate(keyProtectionCertificate);
```

Do not store credential data or keys in the repository, public web root, image layers, shared user folders, or broadly accessible/synchronized storage. The sample defaults to a local `.data` directory for development only. Set `Connector__DataDirectory` to an appropriately protected non-synchronized service directory for real use.

## Outbound Network And Secrets

Provider API roots are explicit, server-configured HTTPS origins on port 443. Actions cannot submit absolute destinations. Raw proxy endpoints are relative and base-path-confined, with encoded path injection rejected. API authentication headers are applied by the server, not passed through from proxy callers.

The default transport resolves DNS during socket connection, rejects private/reserved addresses (including metadata-service IPs and IPv4-mapped IPv6), and connects to an address that was actually validated. HTTP redirects, automatic cookies, and environment proxies are disabled. TLS certificate validation is not bypassed. These defaults intentionally prevent private-network self-hosted providers; do not disable them casually. This is not a complete firewall or a substitute for an allowlisted outbound network policy.

Credentials only travel to the configured provider API and OAuth endpoints. Host-configured endpoint overrides are privileged: a malicious public endpoint configured by an administrator could receive secrets. No configuration override should be writable by ordinary tenant users. Regional, sovereign, or enterprise endpoints must be reviewed explicitly.

The connector disables its HttpClient factory loggers, discards provider error bodies, and emits audit metadata without input/output payloads, token values, or request headers. **Your host, APM, reverse proxy, or custom HTTP handlers can still log secrets.** Suppress authorization headers, credential bodies, OAuth callback query strings, token requests, and provider URLs containing query API keys. The sample suppresses ASP.NET request diagnostics that would otherwise include OAuth query values. Treat action outputs as potentially sensitive business data.

## OAuth And Concurrency

Register exact HTTPS callbacks (loopback HTTP is allowed only for development where the provider permits it). Client IDs/secrets, endpoints, scopes, and redirect URIs come only from server configuration. Avoid unnecessarily broad scopes. Do not confuse connector runtime/admin tokens with provider tokens.

Pending state is random, bounded, expiring, single-use, and bound to the tenant/provider/alias chosen by an authorized administrator. Connection mutations and subsequent authorization attempts fence older callbacks. Bounded striped connection locks may conservatively invalidate another pending authorization sharing the same stripe; retry that authorization rather than bypassing the guard. Restarting the process loses pending states, not stored encrypted connections.

Refresh calls and connection changes are coordinated within one process. Token exchanges and provider operations are not automatically retried. A timeout, disconnection, or caller cancellation can happen after a provider has completed a write; determine its outcome before retrying. There is no durable idempotency guarantee.

Use one application instance with the default store/coordination. Multi-instance production needs distributed state and locks with durable fencing; a shared filesystem alone is not sufficient. Local disconnect is not provider-side token revocation.

## Supply Chain And Release

The production project has no third-party runtime package dependencies. SonarAnalyzer.CSharp is a private build dependency and does not flow to consumers. All projects have transitive lock files and use nuget.org source mapping. Keep the .NET SDK/runtime and packages patched, review any added dependencies, run `./eng/Test-Security.ps1`, and review publish outputs before distribution. Restore audits direct and transitive dependencies at every severity and fails on reported vulnerabilities or unavailable audit data. See [security scanning](docs/SECURITY-SCANNING.md) for external scanner setup and limitations.

This is an initial implementation. Automated fixtures are not proof of live-provider correctness or production security. Run a deployment-specific threat review, sandbox integration tests, key-recovery tests, and egress checks before exposing credentials. Report vulnerabilities privately through your product's established security channel; do not publish provider credentials in an issue.
