# Security review — October 27, 2026

Scope: the library, sample server, tests, dependency graph, and GitHub workflows in this working tree. This is a local review and analyzer assessment, not a certification or a claim that every commercial scanner passes.

## Findings addressed

| Observation | Change |
| --- | --- |
| The machine selected SDK 10.0.203 and shared runtime 10.0.7, below Microsoft's current security release. NuGet's package audit does not assess that installed shared runtime. | Require SDK 10.0.403 and runtime 10.0.14 for repository builds, sample execution, and tests; update TestHost to 10.0.14. Deployment owners and library consumers must update their hosts. |
| OAuth exchange results were saved before complete credential validation; malformed refresh tokens and excessive token/scope values could enter storage. | Validate exchanged credentials before saving or replacing them, bound credential fields, and return sanitized errors for malformed provider fields. |
| Input JSON accepted duplicate credential and connection selector properties. | Reject duplicate properties before connection mutation. |
| The sample processed cleartext HTTP in its default Production environment. | Reject HTTP outside Development and enable HSTS. Development HTTP remains an explicit local testing option. |
| Security analysis and dependency update coverage relied on SDK defaults and manual commands. | Pin the private Sonar analyzer, enable SDK security rules, explicitly enforce all-severity transitive NuGet auditing, lock all projects, preserve SARIF reports, and add CodeQL and Dependabot configuration. |
| The publishing workflow used a mutable action tag for NuGet authentication. | Pin the action to a verified commit. |

The review also checked existing tenant scoping, admin policies, deny-by-default grants, OAuth state/PKCE/fencing, outbound address validation and DNS pinning, redirect/cookie/proxy restrictions, encrypted credential storage, bounded request/response handling, and sanitized provider errors. The existing controls remain covered by the regression suite.

## Verification and limits

Verified with SDK 10.0.403 and .NET/ASP.NET Core 10.0.14:

- 112 tests passed; none failed or were skipped.
- Release build: zero warnings and errors. SARIF contains zero unsuppressed warnings/errors, three informational performance suggestions, and the documented maintainability suppressions.
- Fresh direct/transitive NuGet audit: zero known vulnerable packages across all three projects.
- Live sample checks: Production rejects HTTP with 400; Development serves HTTP and accepts a valid runtime token.
- NuGet packaging passed. The Veracode ZIP contains six allowlisted application files and declares runtime 10.0.14.
- Workflow YAML parsing and `git diff --check` passed. A limited working-tree secret-pattern check found no matches; this does not replace a dedicated history scan.

The repeatable verification command is `./eng/Test-Security.ps1 -PrepareVeracode`. It produces analyzer SARIF, a dependency audit, test results, and an allowlisted Veracode archive. Build warnings and errors fail verification. Reviewed maintainability suppressions are described in [the scanning guide](SECURITY-SCANNING.md); no vulnerability rules are suppressed.

The machine's system-wide SDK/runtime installation was not changed. Verification uses an isolated SDK from Microsoft's official release archive, checked against its published SHA-512 hash. Existing applications continue to use their configured runtimes until deployment owners update them.

SonarQube server quality gates, Veracode analysis, CodeQL execution, Git history secret scanning, deployment DAST, and credentialed live-provider testing have not been performed here. Their results must not be inferred from the local checks. External services, credentials, and deployment controls are described in [the scanning guide](SECURITY-SCANNING.md) and [SECURITY.md](../SECURITY.md).

Runtime baseline source: [Microsoft .NET 10 security update, October 25, 2026](https://support.microsoft.com/en-us/servicing/dotnet/net-10/2026/net-10-0-update-october-25-2026).
