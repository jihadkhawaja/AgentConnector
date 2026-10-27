# Vault and database credential storage

A runnable ASP.NET Core host with two implementations of `ICredentialStore`:

| Store | Credential persistence | Encryption |
| --- | --- | --- |
| [SQLite](SqliteCredentialStore.cs) | A database row for each tenant/provider/connection name. | The complete payload is encrypted and authenticated with ASP.NET Core Data Protection, bound to that connection's identity. |
| [HashiCorp Vault KV v2](VaultCredentialStore.cs) | A versioned Vault secret for each tenant/provider/connection name. | Vault manages encryption at rest; connections to Vault use HTTPS outside the explicit development loopback exception. |

Both stores support create/replace, read, tenant-scoped metadata listing, and deletion. OAuth callbacks and token refreshes use the same `SetAsync` implementation, including rotated refresh tokens. The main AgentConnector package has no new runtime dependencies; `Microsoft.Data.Sqlite` and its SQLite native dependencies belong to this sample and its tests.

## Build and configure the host

Use the SDK/runtime required by the [repository](../../README.md#get-started) and PowerShell 7. From the repository root:

```powershell
dotnet restore AgentConnector.slnx --locked-mode
dotnet build AgentConnector.slnx -c Release --no-restore

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Connector__RuntimeToken = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$env:Connector__AdminToken = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

Choose one of the stores below. Both run the same host on port 5190, use the original sample's fixed `sample-tenant` identity, and grant only `github.get_current_user`. This bearer-token handler is a development example; configure your own authentication and trusted tenant claims in a real host.

## SQLite database

No database server is required. In the configured terminal:

```powershell
$env:Connector__CredentialStore = 'Sqlite'
$env:Connector__DataDirectory = Join-Path ([System.IO.Path]::GetTempPath()) 'agentconnector-storage-sample'

dotnet run --project samples/AgentConnector.StorageSample -c Release --no-build --no-restore --urls http://127.0.0.1:5190
```

The sample creates `credentials.db` and a `keys` directory under `Connector__DataDirectory`. The database has hashed tenant and connection identifiers as its composite primary key and an encrypted `payload` BLOB. SQL parameters handle identifiers and payloads; upserts replace credentials atomically. Listing decrypts only rows for the requested tenant and returns metadata without tokens. Each operation opens its own connection; the singleton does not hold a scoped database context.

The temporary directory above is for synthetic development data. For real credentials, choose a persistent directory outside repositories, synchronized folders, and the web root, with service-account-only permissions. Back up and protect the key ring separately: a database backup without the corresponding keys cannot restore credentials.

On Windows, the host protects persisted keys with DPAPI for the current identity. On other platforms, Development permits unencrypted key files; outside Development, this sample requires a PKCS#12 certificate through `Connector__KeyProtectionCertificatePath` and, if needed, `Connector__KeyProtectionCertificatePassword`. Provision and protect the certificate/private key through your deployment. Outside Development, the host also requires an explicit data directory and HTTPS ingress. See [key custody](../../SECURITY.md#encryption-key-custody).

This is payload encryption, not whole-database encryption. Database structure and record counts remain visible; plaintext does not appear in credential rows. The SQL storage example can be adapted to PostgreSQL or SQL Server by changing the connection/command implementation and schema/upsert syntax while preserving identity-bound encryption and tenant filtering. No PostgreSQL or SQL Server adapter is included.

SQLite's [ADO.NET async methods execute synchronously](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async). The sample keeps the asynchronous store contract, checks cancellation through the operations, and uses a short database busy timeout; it is not a high-throughput database benchmark or a guarantee of interrupting an in-flight SQLite call.

## HashiCorp Vault KV v2

Install the [Vault CLI](https://developer.hashicorp.com/vault/docs/install), or use your existing Vault service with a KV v2 mount. The example expects a single-segment mount name (`secret` by default) and a mount that does not require check-and-set writes. The adapter does not implement CAS, Vault Enterprise namespaces, Vault login, or Vault-token renewal.

### Start a disposable development Vault

In a separate terminal:

```powershell
vault server -dev -dev-listen-address=127.0.0.1:8200
```

[Vault dev mode](https://developer.hashicorp.com/vault/docs/concepts/dev-server) uses disposable in-memory storage and prints a generated root token. Use only synthetic credentials with this mode. Stopping Vault loses its data.

In the host's configured terminal, use that generated root token to provision a policy-limited application token:

```powershell
$env:VAULT_ADDR = 'http://127.0.0.1:8200'
$env:VAULT_TOKEN = Read-Host 'Generated development Vault root token' -MaskInput

vault policy write agentconnector-sample ./samples/AgentConnector.StorageSample/vault-policy.hcl
if ($LASTEXITCODE -ne 0) { throw 'Could not write the development Vault policy.' }

$issuedToken = vault token create -policy=agentconnector-sample -no-default-policy -ttl=1h -format=json
if ($LASTEXITCODE -ne 0) { throw 'Could not issue the application Vault token.' }
$env:Vault__Token = ($issuedToken | ConvertFrom-Json).auth.client_token
Remove-Item Env:VAULT_TOKEN
Remove-Variable issuedToken

$env:Connector__CredentialStore = 'Vault'
$env:Vault__Address = 'http://127.0.0.1:8200'
$env:Vault__Mount = 'secret'
$env:Vault__AllowDevelopmentLoopbackHttp = 'true'

dotnet run --project samples/AgentConnector.StorageSample -c Release --no-build --no-restore --urls http://127.0.0.1:5190
```

The [policy](vault-policy.hcl) grants read/create/update on `secret/data/agentconnector/*` and list/delete on `secret/metadata/agentconnector/*`. Change both policy paths if the mount changes. It covers connections owned by this trusted host, rather than enforcing individual application tenants inside Vault. The adapter validates connection identities and hashes the tenant and provider/name segments before constructing paths.

The implementation follows the [KV v2 API](https://developer.hashicorp.com/vault/api-docs/secret/kv/kv-v2). Listing reads the tenant's hashed metadata directory, then retrieves the matching records to return connection metadata. Deletion uses the **metadata** endpoint to permanently remove all stored versions, rather than soft-delete the latest token version. Deleting a connection does not revoke its token at the provider. Vault backup retention remains a deployment concern.

### Use an existing Vault

Set `Vault__Address` to its HTTPS origin, `Vault__Mount` to the KV v2 mount, and `Vault__Token` through your secret manager or environment. Keep `Vault__AllowDevelopmentLoopbackHttp` unset. The configured address cannot contain a path, query, user information, or fragment. HTTP is accepted only for a numeric loopback address, when explicitly enabled in Development. Normal TLS certificate validation remains enabled.

Provision a narrowly scoped application token with the supplied policy or equivalent. This sample reads that token at startup; your deployment must handle its expiry/renewal and supply a replacement through a restart, or extend the adapter with an appropriate token provider. Do not run the application with a production root token. Vault credentials are distinct from the sample's runtime/admin tokens and the provider credentials stored inside Vault.

Vault is a trusted infrastructure destination and may be on a private network; it uses a separate named HTTP client, without redirects, cookies, environment proxies, or HTTP client logging. This does not change AgentConnector's public-address restrictions for outbound provider API calls. Responses are bounded to 1 MiB, operations have a 30-second deadline, and metadata listing is capped at 1,000 connections per tenant in this sample.

## Exercise storage without a provider account

While either host is running, open a second terminal and set `Connector__AdminToken` and `Connector__RuntimeToken` to the same values you configured for the host. Do not put them in source or shell history; use your secret manager or masked input:

```powershell
$env:Connector__AdminToken = Read-Host 'Sample admin token' -MaskInput
$env:Connector__RuntimeToken = Read-Host 'Sample runtime token' -MaskInput

./samples/AgentConnector.StorageSample/Test-Storage.ps1
```

The script saves a synthetic GitHub credential under a unique connection name, replaces it, lists metadata with the runtime token, and deletes it in a `finally` block. It does not call GitHub or print credential payloads. Regression tests separately verify reading updated credentials, tenant/connection isolation, ciphertext swapping, cancellation, Vault error redaction, response bounds, and invalid/duplicate JSON.

For a real GitHub call, store an appropriate GitHub token using `PUT /connector/api/connections/github`, then call `POST /connector/v1/actions/github.get_current_user` with `{"input":{}}` and the runtime token. Use the same `connectionName` for saving and execution; the default is `default`. Real provider credentials require live-account validation and HTTPS; never use them with disposable Vault dev mode.

## Reuse the stores in your host

The [host's Program.cs](Program.cs) shows the registrations, key protection, HTTP transport configuration, and database initialization. Register your chosen `ICredentialStore` **before** `AddAgentConnector`. `CredentialDirectory` is unused by both of these implementations. If copying the adapters, include [StoredCredential.cs](StoredCredential.cs) and, for Vault, [VaultStoreOptions.cs](VaultStoreOptions.cs). Database hosts also need the sample's SQLite package reference.

Both stores are designed for one application instance. Shared database/Vault storage does not distribute OAuth state, connection locks, or refresh coordination. Multi-instance deployments still need distributed state and locks with durable fencing; see [OAuth and concurrency](../../SECURITY.md#oauth-and-concurrency). These samples do not include a migration framework, production identity system, managed Vault authentication, or a production key-management service integration.
