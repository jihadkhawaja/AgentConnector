# Security scanning

Clean scan results apply to the rules, advisory data, code revision, and configuration actually tested. They do not establish zero risk. A local Roslyn analyzer run is not a SonarQube server quality gate, a Veracode assessment, or a penetration test.

## Repeatable local checks

With .NET SDK 10.0.403 or newer, .NET/ASP.NET Core runtime 10.0.14 or newer, and PowerShell 7 installed, run from the repository root:

```powershell
./eng/Test-Security.ps1 -PrepareVeracode
```

This command performs a locked restore with fresh NuGet advisory data, builds every solution project with the pinned Sonar C# analyzer and SDK security rules, runs the tests, and checks the machine-readable dependency audit. Warnings fail the build. NuGet audit failures and vulnerabilities of any severity fail restore. No vulnerability rules are suppressed.

Reports are written under `artifacts/security/reports/`:

| File | Purpose |
| --- | --- |
| `AgentConnector.sarif` | Library compiler and analyzer diagnostics |
| `AgentConnector.Server.sarif` | Sample server compiler and analyzer diagnostics |
| `AgentConnector.Tests.sarif` | Test compiler and analyzer diagnostics |
| `dependencies.json` | Direct and transitive NuGet vulnerability audit |
| `veracode.zip` | Optional compiled application package for Veracode |

Three method-scoped Sonar code-quality exceptions are documented in source: canonical provider URLs are deliberate catalog defaults (`S1075`), and two catch paths intentionally omit exception details to avoid logging secrets (`S6667`). These are reviewed maintainability exceptions, not vulnerability suppressions.

## SonarQube

Install your server-compatible SonarScanner for .NET and configure a project, server URL, and token through your secret manager. From the repository root, run a scanner `begin`, a full build and tests, then scanner `end`. For example, after setting `SONAR_PROJECT_KEY`, `SONAR_HOST_URL`, and `SONAR_TOKEN` in your shell:

```powershell
dotnet sonarscanner begin "/k:$env:SONAR_PROJECT_KEY" "/d:sonar.host.url=$env:SONAR_HOST_URL" "/d:sonar.token=$env:SONAR_TOKEN" /d:sonar.qualitygate.wait=true
dotnet restore AgentConnector.slnx --locked-mode
dotnet build AgentConnector.slnx -c Release --no-restore --no-incremental
dotnet test AgentConnector.slnx -c Release --no-build --no-restore
dotnet sonarscanner end "/d:sonar.token=$env:SONAR_TOKEN"
```

Check each command's exit status in your CI runner. The scanner may change analyzer settings according to the server's quality profile. Configure the server gate to reject security findings on the whole project, and review all security hotspots. Configure coverage collection and import separately if your gate requires coverage; the commands above do not collect it. Do not exclude production code to obtain a clean result.

Reference: [SonarScanner for .NET](https://docs.sonarsource.com/sonarqube-server/analyzing-source-code/scanners/dotnet/introduction), [quality gate wait parameter](https://docs.sonarsource.com/sonarqube-server/analyzing-source-code/analysis-parameters/parameters-not-settable-in-ui).

## Veracode

The optional ZIP includes the library and sample application DLLs, portable PDBs, dependency manifest, and runtime configuration. It uses a framework-dependent Debug publish with optimization disabled, and an explicit file allowlist excludes tests, credentials, keys, and local configuration. It contains no third-party runtime DLLs because the application has no third-party runtime dependencies.

Submit this package through your licensed Veracode account and select both application modules for analysis. Review prescan results for missing modules and dependencies, run Static Analysis and SCA as licensed, and enforce your policy against all findings. Generating the archive does not run Veracode or imply a passing result.

Reference: [Veracode .NET packaging requirements](https://docs.veracode.com/r/compilation_net).

## CI and deployment checks

The existing .NET workflow runs the local checks on Windows and Linux and preserves the reports and scan package. The CodeQL workflow adds C# `security-extended` analysis on pushes, pull requests, and weekly. Dependabot checks NuGet and GitHub Action versions weekly. CodeQL requires code scanning to be enabled and the appropriate GitHub entitlement for private repositories. Configure branch protection and code scanning merge protection in GitHub; committing a workflow does not enable those account settings.

The repository requires SDK 10.0.403 and sets runtime 10.0.14 as the minimum for the sample and tests, following Microsoft's October 25, 2026 security update. Deployments must install both the patched .NET and ASP.NET Core shared runtimes. Library consumers must enforce that minimum in their own hosts; a library package cannot update a host runtime. See [Microsoft's .NET 10 security release](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).

These checks do not continuously assess installed shared runtimes against future CVEs, scan Git history for secrets, run credentialed provider integrations, exercise deployment-specific DAST, or validate production identity, CSRF, TLS, filesystem permissions, Data Protection key custody, or egress controls. Those checks remain necessary under [SECURITY.md](../SECURITY.md). SonarQube and Veracode server scans require the corresponding services and credentials.
