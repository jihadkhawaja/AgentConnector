# Compatibility And Remaining Work

AgentConnector is a native .NET 10 connector gateway. It runs in the host process without Docker, container images, or a hosted connector service.

## Implemented

- Native `AddAgentConnector` DI registration and `MapAgentConnector` ASP.NET routing.
- Direct C# connection management and action execution.
- 48 explicitly defined provider adapters and 158 named actions, with input-schema discovery.
- Named connections, tenant-isolated encrypted credential records, and metadata-only listing.
- Host authentication and separate runtime/admin authorization policies.
- Default-deny action and proxy grants; block rules override action grants.
- OAuth authorization-code initiation/callback, PKCE where configured, Basic/form/JSON client exchange, and serialized token refresh.
- Single-use, ten-minute, capacity-bounded, in-memory OAuth state with connection-change fencing.
- HTTPS-only outbound requests; DNS/IP validation at the actual socket connection; no redirects, cookies, environment proxy, or disabled TLS validation.
- Request/response size limits, timeouts/cancellation, safe provider error envelopes, and rate-limit retry timing.
- Structured metadata-only audit events and replaceable credential/audit storage interfaces.
- Policy-filtered OpenAPI 3.1 action documents.

## Not Implemented

- Providers and actions beyond those listed in the coverage documentation.
- Third-party SDK/CLI protocol compatibility or built-in runtime-token/JWT issuance.
- MCP transport/tools, dashboards, provider triggers, webhook signature verification, subscription maintenance, and file transit.
- SQLite/PostgreSQL schema migrations, S3 transit storage, durable runtime token records, persistent run browsing, or managed SaaS OAuth.
- Durable idempotency or exactly-once provider writes. Requests with `Idempotency-Key` are explicitly rejected instead of silently giving a false guarantee.
- Opaque connection-ID selectors. Select a named connection instead.
- Automatic retries, pagination, OAuth revocation, device authorization, client-credentials acquisition, OAuth1, and every provider-specific authentication scheme.
- Distributed OAuth state, refresh locks, durable reconnect revisions, or cross-process credential-write concurrency.
- Binary/multipart uploads, SSE, streaming AI output, native SDK-specific signing, or NativeAOT certification.
- Per-user/per-connection grants within a tenant. The host must enforce these before permitting shared-tenant credential use.
- Live-account certification for credentialed providers, a compliance certification, or a penetration test.

The built-in file store and coordination are for **one process / one application instance**. Changing to a shared database alone does not make the runtime safe for multi-instance OAuth. Implement distributed pending state and connection revisions as well as refresh coordination before scaling horizontally.

The HTTP API uses `/v1/actions/{id}` and a success envelope. Third-party clients must target the documented contracts. Unknown JSON request properties, conflicting aliases, unsupported stable-ID selectors, and unsupported idempotency headers fail explicitly. Authorization failures produced by the host may use the host's response format rather than the connector envelope.

Disconnect deletes local credentials and invalidates pending authorization; it does not revoke access at the provider, cancel requests already dispatched with credentials, delete remote resources, or erase provider grants. Revoke grants in the provider console when necessary.

## Validation Boundaries

The local test suite checks policy rules, all declared action request builders, authentication headers, input validation, private/reserved address filtering, storage encryption/isolation, bounded responses, timeout/cancellation, rate limits, no automatic retries, OAuth PKCE/replay/connection-change behavior, concurrent refresh, ASP.NET policies, and secret-free metadata.

Network tests validate the socket-guard policy and handler settings; they are not a live DNS-rebinding penetration test. Live account credentials, provider consent approval, provider rate limits, account entitlements, external API behavior, deployment key custody, and actual egress firewall behavior require environment-specific validation before production.