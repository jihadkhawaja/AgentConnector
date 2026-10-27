# Jira and Confluence: Atlassian OAuth sample

Connect agents to **Atlassian Cloud** using delegated OAuth 2.0 (3LO). A human selects **Sign in to Jira** or **Sign in to Confluence**, signs in and consents in an Atlassian browser popup, and returns to the sample callback. The server exchanges the authorization code and encrypts the resulting credentials. Agents call AgentConnector with a separate runtime token and a connection name; they never receive Atlassian access tokens, refresh tokens, or the app's client secret.

The example enables five read actions. Issue/page creation is an explicit extension described below. It uses the existing sample host, one fixed tenant, one trusted Atlassian site, and two separately stored provider connections. It does not cover Jira/Confluence Data Center or unattended client-credentials authentication.

## 1. Register the integration in the developer console

The **application developer/operator** registers the app once. End users only sign in and consent. Do not require every customer to create their own app or provide API tokens. App registration is in the [Atlassian developer console](https://developer.atlassian.com/console/myapps/); organization/site access is managed separately in [Atlassian Administration](https://admin.atlassian.com/).

1. In the developer console, select **Create → OAuth 2.0 integration**. Name the integration and accept the applicable terms. If offered a grant-type choice, choose a resource-level/site-restricted grant for this single-site sample.
2. Open **Permissions**. Add **Jira API** and **Confluence API**, then configure the scopes in the table below. Jira's listed scopes are classic scopes; the Confluence v2 endpoints use the granular scopes shown.
3. Open **Authorization → OAuth 2.0 (3LO) → Configure**. Register this exact callback for the local HTTPS example:

   ```text
   https://localhost:7188/atlassian/oauth/callback
   ```

   Save the callback. Scheme, hostname, port, and path must match the server configuration. This sample's HTML callback differs from the library's default JSON callback at `/connector/oauth/callback`.
4. Open **Settings** and obtain the **Client ID** and **Secret**. Keep the secret in server environment variables or a secret manager. Do not put it in JavaScript, agent prompts, source control, or screenshots.
5. For testing as the app owner, the app may remain private. To let other users consent, open **Distribution** and enable sharing, completing the console's requested app information. Sharing does not grant access to a user's sites or bypass an organization's restrictions.

These steps follow Atlassian's [Jira 3LO guide](https://developer.atlassian.com/cloud/jira/platform/oauth-2-3lo-apps/) and [Confluence 3LO guide](https://developer.atlassian.com/cloud/confluence/oauth-2-3lo-apps/).

## 2. Grant only the permissions the agent needs

| AgentConnector action | Scope to add to the app/request | Signed-in user's permissions |
| --- | --- | --- |
| `jira.get_current_user` | `read:jira-user` | Access to Jira |
| `jira.list_projects` | `read:jira-work` | Access to the projects returned by Jira |
| `jira.get_issue` | `read:jira-work` | Browse Projects and any applicable issue-security access |
| `confluence.list_spaces` | `read:space:confluence` | Access to the spaces returned by Confluence |
| `confluence.get_page` | `read:page:confluence` | View permission for the space and page, including restrictions |
| Refresh after token expiry | `offline_access` in the authorization request | Continued consent and site access |

`offline_access` is requested by the sample to obtain a refresh token; it is not an extra Jira or Confluence content scope to find in their permission lists. The server saves rotated refresh tokens and coordinates refresh within one process.

The administrator should grant the user product access, project access, and space/page access in the normal Atlassian administration screens. Organization policies and app access rules can require administrator approval or block the integration. The app does not need Jira administration, Confluence administration, organization administration, or write scopes for these examples. Scopes set an upper bound; they do not elevate the user's permissions.

The exact scope set in [AtlassianSample.cs](AtlassianSample.cs) is:

```text
read:jira-user read:jira-work read:space:confluence read:page:confluence offline_access
```

Both provider sign-ins request this union because subsequent consent for the same client/account can replace the previous scope grant. If your application only supports one product, remove the other product's OAuth client, actions, scopes, and sign-in button. Changes to scopes require renewed consent.

Scope references: [Jira current user](https://developer.atlassian.com/cloud/jira/platform/rest/v3/api-group-myself/#api-rest-api-3-myself-get), [Jira projects](https://developer.atlassian.com/cloud/jira/platform/rest/v3/api-group-projects/), [Jira issues](https://developer.atlassian.com/cloud/jira/platform/rest/v3/api-group-issues/), [Confluence spaces](https://developer.atlassian.com/cloud/confluence/rest/v2/api-group-space/), and [Confluence pages](https://developer.atlassian.com/cloud/confluence/rest/v2/api-group-page/).

## 3. Identify the trusted Atlassian site

Find the site's cloud ID using either method in Atlassian's [cloud ID guide](https://support.atlassian.com/jira/kb/retrieve-my-atlassian-sites-cloud-id/):

- Open `https://YOUR-SITE.atlassian.net/_edge/tenant_info` in your browser and copy the `cloudId` value.
- In Atlassian Administration, choose your organization, then **Apps → Sites**, and open the site. The identifier after `/s/` in the address bar is its cloud ID. Do not use the organization ID after `/o/`.

The cloud ID is a UUID, for example `00000000-0000-4000-8000-000000000001` (synthetic). The sample pins these trusted server-side API roots:

```text
https://api.atlassian.com/ex/jira/CLOUD_ID/
https://api.atlassian.com/ex/confluence/CLOUD_ID/
```

Choose that same site on the consent screen. The callback confirms code exchange; it does not discover sites or verify site membership. The first read action verifies access to the configured site. For a multi-site product, implement a server-side site chooser using Atlassian's `GET https://api.atlassian.com/oauth/token/accessible-resources` with the current access token, validate the returned resource's scopes and identity, and authorize the site's selection before configuring separate runtimes/providers. Do not let agents submit an arbitrary provider URL. The library has one configured base URI per provider/runtime, not per connection.

## 4. Configure and run the sample

Use the SDK and runtimes required by [global.json](../../global.json) and [Directory.Build.props](../../Directory.Build.props). Run from the repository root in PowerShell 7. Trust the local HTTPS development certificate if needed:

```powershell
dotnet dev-certs https --trust
```

Set these environment variables in the same shell. Read real values locally; do not paste them into chat or source files:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Atlassian__ClientId = Read-Host 'Atlassian client ID'
$env:Atlassian__ClientSecret = Read-Host 'Atlassian client secret' -MaskInput
$env:Atlassian__CloudId = Read-Host 'Trusted site cloud ID'
$env:Atlassian__PublicOrigin = 'https://localhost:7188'
$env:Connector__AdminToken = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$env:Connector__RuntimeToken = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
# Keep keys and encrypted credentials outside the repository and synchronized folders.
$env:Connector__DataDirectory = Join-Path $env:LOCALAPPDATA 'AgentConnector/AtlassianSample'

dotnet run --project samples/AgentConnector.Server --urls https://localhost:7188
```

Omitting all `Atlassian__*` settings leaves the original sample unchanged. Providing only some settings fails startup with a configuration error. `PublicOrigin` is a trusted origin without a path; the callback is appended by the server. For loopback HTTP development, use matching HTTP URLs in the console, environment, browser, and command line only if Atlassian accepts the callback. Use HTTPS for deployed hosts; the sample rejects HTTP outside Development.

Open [the sign-in page](https://localhost:7188/atlassian). Enter the generated connector **admin** token using your local secret tooling and use `work` as the connection name. To transfer it locally on Windows, you can run `Set-Clipboard -Value $env:Connector__AdminToken` in the configured shell before starting the host; paste into the page and clear the clipboard afterward. The page keeps the token in the password input's memory, never in local/session storage or a URL. This is a local administrator demo: a production app must replace the shared-token field with its own authenticated account and connection-authorization UI.

## 5. Complete popup sign-in

1. Select **Sign in to Jira**. A popup opens immediately from the click; allow popups for the sample origin if necessary.
2. The page sends an admin-authenticated `POST /connector/api/oauth/jira/authorize` with `{"connectionName":"work"}`. The library creates random, expiring, single-use state bound to the trusted tenant, provider, and connection name.
3. The popup navigates to `https://auth.atlassian.com/authorize` with `audience=api.atlassian.com`, `prompt=consent`, and the configured scopes. The human signs in, selects the configured site, and grants access.
4. Atlassian redirects the popup to `/atlassian/oauth/callback?code=...&state=...`. The server validates/consumes state, exchanges the code via JSON at `https://auth.atlassian.com/oauth/token`, and saves encrypted credentials. The anonymous callback relies on state; it does not accept tenant/connection overrides.
5. The callback removes query parameters from the visible address, sends only a success/failure flag to the exact same origin using `postMessage`, and closes the popup. The parent checks both the message origin and popup window identity. No provider tokens or authorization code are sent in the message.
6. Select **Check connections**, then run a read action below. Repeat **Sign in to Confluence** with connection name `work`. The two provider connections are stored separately even though they share an app and site.

The sample explicitly uses Atlassian's documented confidential-client flow with a server-held client secret, JSON token requests, and `UsePkce: false`. Atlassian's referenced 3LO instructions do not specify a PKCE exchange. Other providers retain their configured PKCE behavior.

Callbacks use `no-store`, a restrictive content-security policy, and `no-referrer`. The sample suppresses ASP.NET request diagnostics; also exclude callback query strings, authorization headers, and token request bodies from reverse-proxy/APM logs. If a browser or provider opener policy prevents messaging, the callback displays a close-window instruction; return to the parent and select **Check connections**. Closing a popup early does not prove the server failed to save a connection, so check before retrying.

## 6. Call Jira and Confluence from an agent

Provision only the connector **runtime** token to the agent's HTTP tool. Keep the admin token and Atlassian client secret with the host/operator. The fixed `sample-tenant` comes from the sample bearer handler, not the request body. The agent selects `connectionName: "work"`.

The runnable [Invoke-AtlassianReadExamples.ps1](Invoke-AtlassianReadExamples.ps1) calls Jira profile/projects and Confluence spaces. Optional arguments also read one issue and page. Run it in a second PowerShell session with `Connector__RuntimeToken` securely provisioned to that session:

```powershell
./samples/AgentConnector.Server/Invoke-AtlassianReadExamples.ps1 `
    -BaseUri https://localhost:7188 -ConnectionName work -IssueKey DEMO-123 -PageId 123456
```

Example HTTP requests for the agent's tool:

```http
POST /connector/v1/actions/jira.get_issue
Authorization: Bearer <connector-runtime-token>
Content-Type: application/json

{"connectionName":"work","input":{"issue_id":"DEMO-123"}}
```

```http
POST /connector/v1/actions/confluence.get_page
Authorization: Bearer <connector-runtime-token>
Content-Type: application/json

{"connectionName":"work","input":{"page_id":"123456"}}
```

Discover permitted tools with `GET /connector/v1/actions?service=jira` or `?service=confluence`, or use `GET /connector/openapi.json`, all with the runtime bearer token. Do not give the agent management routes. Results contain provider-native JSON; respect data access rules when passing business data to a model. The current page action returns the default Confluence page representation; it does not request a particular body format or follow pagination automatically.

For an agent embedded in your authenticated ASP.NET Core host, inject `ConnectorRuntime` and execute the same actions directly:

```csharp
// trustedTenantId must come from your authenticated host identity/authorization layer.
var issue = await connector.ExecuteAsync(trustedTenantId, "jira.get_issue",
    new() { ["issue_id"] = "DEMO-123" }, "work", cancellationToken);
var page = await connector.ExecuteAsync(trustedTenantId, "confluence.get_page",
    new() { ["page_id"] = "123456" }, "work", cancellationToken);
```

Direct service calls must enforce authorization themselves. For per-user isolation, map authenticated users to trusted tenant identities or implement explicit connection-resource authorization. All authorized runtime users in the sample's fixed tenant can use that tenant's connections.

## Optional: allow an agent to create issues or pages

Enable writes only when the application's agent approval policy permits them. Add the corresponding scope to the console and to **both** clients' shared scope list in `AtlassianSample.Configure`, add the exact action to `AllowedActions`, restart, and repeat consent:

| Action to allow | Additional OAuth scope | User permission |
| --- | --- | --- |
| `jira.create_issue` | `write:jira-work` | Browse Projects and Create Issues in the target project |
| `confluence.create_page` | `write:page:confluence` | View the target space and create pages there |

For Jira creation, call the action with this example input (replace the project and issue type with values supported by your site):

```json
{"connectionName":"work","input":{"body":{"fields":{"project":{"key":"DEMO"},"issuetype":{"name":"Task"},"summary":"Agent-created example"}}}}
```

For Confluence creation, use the target space's numeric ID:

```json
{"connectionName":"work","input":{"body":{"spaceId":"123456","status":"current","title":"Agent example","body":{"representation":"storage","value":"<p>Created with explicit write access.</p>"}}}}
```

Raw proxies remain denied. Do not use blanket `jira.*` or `confluence.*` action grants to enable a single operation. Provider writes have no durable idempotency guarantee; inspect the outcome after a timeout before retrying.

## Troubleshooting and deployment

| Symptom | Check |
| --- | --- |
| No popup | Allow popups for the sample origin; initiate sign-in with the button. |
| Callback mismatch | Use the exact configured public origin and `/atlassian/oauth/callback` in the console; open the page using that same hostname and port. |
| Consent rejects scopes | Add the scopes under the correct APIs in the console; re-consent after a change. |
| Other users cannot install | Enable app sharing and check organization approval/access policies. |
| API returns 401/403/404 | Check the selected site versus configured cloud ID, user product/project/page permissions, and app restrictions. A stored connection alone does not prove site access. |
| Callback fails or expires | Start again; state is single-use, expires after ten minutes, and is lost on restart. Connection changes invalidate pending callbacks. |
| Refresh fails | Consent may have been revoked or the refresh token expired. Sign in again. Run one host instance with the default coordination. |

Use persistent protected Data Protection keys, a protected non-synchronized data directory, HTTPS, and a real host identity system before deployment. On Windows the sample protects its persisted keys with DPAPI; other platforms require explicit key protection. Local deletion via `DELETE /connector/api/connections/jira?connectionName=work` (or `confluence`) requires the admin token and removes local credentials only. Revoke provider consent in the user's Atlassian connected-app settings when needed; disconnect both local records if retiring the shared integration.

Read [SECURITY.md](../../SECURITY.md) for host obligations. The sample has synthetic regression tests for authorization, JSON code exchange, callback failures/replay, trusted site paths, and rotating refresh tokens. Live Atlassian consent/API calls still require validation with your registered app and test site.
