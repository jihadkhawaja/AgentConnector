# AgentConnector repository guidance

## Project and ownership

AgentConnector is an embeddable .NET 10 / ASP.NET Core library. Read [README.md](README.md) for setup and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) before changing component boundaries.

- `src/AgentConnector/`: library domain models, application services, infrastructure, HTTP endpoints, and dependency injection.
- `samples/AgentConnector.Server/`: example host and authentication; this is not a production identity system.
- `tests/AgentConnector.Tests/`: tests mirroring the library's component folders.
- `eng/`: engineering scripts, including the repeatable security gate.
- `.github/workflows/`: build, release, and CodeQL workflows.
- `artifacts/`: ignored build outputs and security reports; keep generated output out of commits.

Preserve the public `AgentConnector` namespace and existing host contracts. Keep provider HTTP behavior in provider adapters, connection lifecycle in application services, and HTTP authentication/authorization at the host boundary. The production library has no third-party runtime package dependencies; keep analyzer dependencies private.

## Build and verification

Run commands from the repository root with PowerShell 7 and the SDK/runtime versions required by `global.json` and `Directory.Build.props`. Those files are the version source of truth.

```powershell
dotnet restore AgentConnector.slnx --locked-mode
dotnet build AgentConnector.slnx -c Release --no-restore
dotnet test AgentConnector.slnx -c Release --no-build --no-restore
```

For requested security checks and changes affecting security boundaries, use the existing gate:

```powershell
./eng/Test-Security.ps1
```

Add `-PrepareVeracode` when a compiled scan package is needed. The gate includes a fresh transitive dependency audit, the SDK and Sonar C# analyzers, and regression tests. Reports are under `artifacts/security/reports/`. [docs/SECURITY-SCANNING.md](docs/SECURITY-SCANNING.md) explains external scanner setup and limitations.

Check command exit codes. Report missing tools, unsupported runtimes, unavailable advisory feeds, and failed checks instead of bypassing them. When intentionally changing dependencies, regenerate and review the affected `packages.lock.json` files, then verify locked restore. Do not downgrade the runtime floor, turn off auditing, or relax warning enforcement to obtain a passing build.

Use focused regression tests for behavioral fixes and run the security gate for security-sensitive changes. Documentation-only changes need format/link/configuration validation, not a full application test run. Preserve unrelated working-tree edits.

## Security invariants

Read [SECURITY.md](SECURITY.md) when reviewing or modifying these boundaries:

- Tenants come from trusted authenticated claims; management endpoints require the admin policy. Direct application-service callers must enforce their own authorization.
- Actions and raw proxies default to denied. Validate connection identities and credentials before storage or dispatch.
- Outbound destinations stay confined to trusted provider origins and base paths. Preserve HTTPS, public-address validation during socket connection, and DNS pinning. Keep redirects, automatic cookies, and environment proxies disabled in the default transport.
- OAuth state remains random, expiring, bounded, single-use, and fenced against connection mutations. Preserve PKCE where configured and coordinated token refresh.
- Credential records remain encrypted and bound to tenant/connection identity. Key custody and filesystem permissions belong to the deployment; shared storage alone does not make coordination distributed.
- Preserve bounded JSON/request/response handling, duplicate-property rejection, cancellation, sanitized errors, and secret-free logging/auditing. The sample requires HTTPS outside Development.

Never put live credentials, key rings, callback secrets, or token-bearing payloads into source, logs, fixtures, or reports. Use synthetic test values. Keep dependency sources mapped and CI actions pinned to reviewed commits.

## Security scanning agent

The project-scoped custom agent is defined in [.codex/agents/security_scanner.toml](.codex/agents/security_scanner.toml). For requested security reviews or scan troubleshooting, use `security_scanner` when custom agents are available; otherwise follow its workflow directly. Creating this definition does not schedule scans or configure external scanner accounts.

Example request:

> Use security_scanner to review this repository, run the local security gate, and report findings and checks that could not run.

Distinguish current evidence from historical assessments in `docs/SECURITY-REVIEW.md`. Report actual tool/version, scope, findings, fixes, and unperformed checks. A local analyzer run, CodeQL workflow definition, or prepared Veracode archive is not a completed external scan. Do not claim zero risk or universal scanner compliance.
