# Contributing to AgentConnector

Contributions to code, documentation, tests, provider coverage, and accessibility are welcome. Please follow our [code of conduct](CODE_OF_CONDUCT.md).

## Before you start

- Search existing issues and pull requests before opening a new one.
- Use the issue forms for reproducible bugs, feature proposals, and accessibility barriers. Discuss substantial API or architectural changes in an issue before implementing them.
- Report suspected vulnerabilities privately using [SECURITY.md](SECURITY.md). Never include live credentials, key rings, callback secrets, or token-bearing payloads in issues, pull requests, logs, or fixtures.

Read [README.md](README.md) for setup, [AGENTS.md](AGENTS.md) for repository rules, and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) before changing component boundaries. Provider behavior and current limitations are documented in [docs/PROVIDERS.md](docs/PROVIDERS.md) and [docs/COMPATIBILITY.md](docs/COMPATIBILITY.md).

## Development setup

Use PowerShell 7 and the .NET SDK specified in [global.json](global.json). The runtime floor and build settings are defined in [Directory.Build.props](Directory.Build.props); treat those files as the source of truth.

Fork and clone the repository, create a branch for your change, and run these commands from the repository root:

```powershell
dotnet restore AgentConnector.slnx --locked-mode
dotnet build AgentConnector.slnx -c Release --no-restore
dotnet test AgentConnector.slnx -c Release --no-build --no-restore
```

See the README to run the sample host. Its authentication is a development example; use synthetic credentials when reproducing a problem.

## Making a change

- Keep changes focused and preserve the public `AgentConnector` namespace and existing host contracts.
- Keep provider HTTP behavior in provider adapters, connection lifecycle in application services, and HTTP authentication and authorization at the host boundary.
- Follow the existing style and keep tests under the matching component folder in `tests/AgentConnector.Tests/`.
- Add focused regression coverage for behavioral fixes. Update documentation when public behavior, configuration, provider support, or limitations change.
- Preserve the security invariants in [AGENTS.md](AGENTS.md) and [SECURITY.md](SECURITY.md), including tenant isolation, default-deny access, outbound destination validation, OAuth protections, bounded handling, and secret-free diagnostics.
- Keep the production library free of third-party runtime package dependencies and keep analyzer dependencies private. If intentionally changing dependencies, regenerate and review affected `packages.lock.json` files, then verify locked restore.
- Keep generated output in ignored `artifacts/`. Do not commit credentials, local data, build outputs, or scanner reports.

## Verification

Run the restore, build, and test commands above for code changes. For changes affecting security boundaries, also run the existing gate:

```powershell
./eng/Test-Security.ps1
```

Add `-PrepareVeracode` when a compiled scan package is needed. See [docs/SECURITY-SCANNING.md](docs/SECURITY-SCANNING.md) for external scanner setup and limitations. A local gate or prepared package is not a completed external scan.

For documentation-only changes, check formatting, links, and any template or configuration syntax; a full application test run is unnecessary. Report failed checks, missing tools, unavailable advisory feeds, or checks you could not run. Do not relax warning enforcement, auditing, or supported runtime requirements to make a check pass.

## Submitting a pull request

Use the pull request template to explain the problem, resulting behavior, related issue, and verification performed. Include compatibility or deployment implications when applicable. Sanitize any examples and logs. Maintainers may request changes before merging; address review feedback with focused updates.

Contributions are made under the repository's [Apache 2.0 license](LICENSE).
