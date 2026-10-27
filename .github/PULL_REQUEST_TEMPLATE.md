## Problem and change

Describe the problem and the resulting behavior. Link related issues, if any.

## Verification

List the checks performed and their results. Explain checks that could not run.
For code changes, include locked restore, Release build, and tests. For security
boundary changes, include `./eng/Test-Security.ps1`. For documentation-only
changes, include formatting, link, and configuration checks.

## Compatibility and deployment

Describe any public API, configuration, provider behavior, security, accessibility,
or deployment implications. Write "None" if there are no such implications.

## Checklist

- [ ] I followed [CONTRIBUTING.md](https://github.com/jihadkhawaja/agentconnector/blob/main/CONTRIBUTING.md) and the [code of conduct](https://github.com/jihadkhawaja/agentconnector/blob/main/CODE_OF_CONDUCT.md).
- [ ] I added appropriate regression coverage for behavioral changes, or explained why it is unnecessary.
- [ ] I updated affected documentation, or no documentation changes are needed.
- [ ] I excluded live credentials, key rings, callback secrets, sensitive payloads, and generated artifacts.
