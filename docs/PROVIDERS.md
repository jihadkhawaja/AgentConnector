# Provider Coverage

There are **48 adapters and 158 named actions**. These are implemented HTTP request contracts, not catalog-only entries. Automated tests build every action and exercise local security/OAuth contracts with mocked responses. **No credentialed adapter has been live-account certified.** Obtain your own API credentials, register any OAuth apps, grant minimal scopes, and verify your required operations in a provider sandbox before release.

`Bearer` means a provider token is sent as `Authorization: Bearer ...`. For OAuth-capable services, a manually supplied bearer token also works when permitted by the provider. `OAuth` below means built-in authorization-code endpoint metadata, not a managed or hosted OAuth app. Scope selection is server configuration; the provider ultimately enforces its permission model. Built-in actions do not encode a complete provider-specific implied-scope lattice.

| ID | Provider | Named Operations | Authentication / Setup |
| --- | --- | --- | --- |
| `github` | GitHub | Profile, repositories, issues, comments, pull requests, repository search | Bearer PAT / OAuth |
| `gitlab` | GitLab | Profile, projects, issues, merge requests | Bearer / OAuth; trusted base override for self-hosting must still be publicly reachable HTTPS |
| `gmail` | Gmail | Profile, message list/get/send, labels | Google OAuth / bearer |
| `google_drive` | Google Drive | File list/get, create metadata, permissions | Google OAuth / bearer; no binary upload/download |
| `google_calendar` | Google Calendar | Calendars, events, create event | Google OAuth / bearer |
| `google_sheets` | Google Sheets | Spreadsheet metadata, cell values, batch update | Google OAuth / bearer |
| `google_docs` | Google Docs | Get/create document, batch update | Google OAuth / bearer |
| `google_contacts` | Google Contacts | List/create contact | Google OAuth / bearer |
| `google_tasks` | Google Tasks | Task lists, list/create task | Google OAuth / bearer |
| `bigquery` | BigQuery | Datasets, execute query | Google OAuth / bearer; queries may modify data and incur costs |
| `google_analytics` | Google Analytics | Run report | Google OAuth / bearer |
| `outlook` | Microsoft Outlook | Profile, messages, send mail, events | Microsoft Graph OAuth / bearer |
| `onedrive` | Microsoft OneDrive | Files, item metadata, create folder | Microsoft Graph OAuth / bearer |
| `teams` | Microsoft Teams | Teams, channels, send message | Microsoft Graph OAuth / bearer; permissions depend on delegated/application mode |
| `sharepoint` | Microsoft SharePoint | Site search, lists, items | Microsoft Graph OAuth / bearer |
| `slack` | Slack | Auth test, channels/history/users, send message | Bearer / bot-install OAuth; comma-separated scopes |
| `discord` | Discord | Current user, guilds, channel messages/send | `api_key` with full `Bot TOKEN` or `Bearer TOKEN` Authorization header value |
| `notion` | Notion | Current user, search, pages, block children | Bearer / OAuth; pinned Notion API version |
| `linear` | Linear | GraphQL query/mutation | `api_key` raw personal token in Authorization; OAuth uses Bearer |
| `jira` | Atlassian Jira | Profile, projects, get/create issue | Basic email/API token or bearer; trusted instance URI required |
| `confluence` | Atlassian Confluence | Spaces, get/create page | Basic email/API token or bearer; trusted instance URI required |
| `airtable` | Airtable | Bases, list/create records | Bearer PAT / OAuth |
| `asana` | Asana | Profile, workspaces, list/create task | Bearer / OAuth |
| `clickup` | ClickUp | Profile, workspaces, list/create task | `api_key` raw Authorization header |
| `monday` | monday.com | GraphQL query/mutation | `api_key` raw Authorization header |
| `hubspot` | HubSpot | List/create contacts, companies, list/create deals | Bearer private-app token / OAuth |
| `salesforce` | Salesforce | SOQL query, get/create object record | Bearer / OAuth; trusted instance URI required |
| `zendesk` | Zendesk | Profile, list/get/create tickets | Basic `email/token` + API token, or bearer; instance URI required |
| `intercom` | Intercom | Profile, list/create contacts, conversation search | Bearer |
| `stripe` | Stripe | Account, customers, payment intents, subscriptions | Bearer secret key; named actions are read-only, form-encoded writes not supplied |
| `shopify` | Shopify | Admin GraphQL query/mutation | `api_key` X-Shopify-Access-Token; store URI required |
| `paypal` | PayPal | Get/create order, get subscription | Bearer; acquire short-lived client-credentials tokens outside this library |
| `dropbox` | Dropbox | Current account, list files, metadata, create folder | Bearer / OAuth |
| `box` | Box | Current user, files, create folder | Bearer / OAuth |
| `zoom` | Zoom | Current user, list/create meeting | Bearer / OAuth |
| `sendgrid` | SendGrid | Profile, send email, templates | Bearer API key |
| `twilio` | Twilio | Account, messages | Basic account SID + auth token; form-encoded message sends not supplied |
| `openai` | OpenAI | Models, Responses, chat completions, embeddings | Bearer API key; JSON, non-streaming only |
| `anthropic` | Anthropic | Models, Messages | `api_key` x-api-key; JSON, non-streaming only |
| `gemini` | Google Gemini | Models, generate content, embed content | `api_key` x-goog-api-key |
| `mistral` | Mistral AI | Models, chat completions, embeddings | Bearer API key |
| `groq` | Groq | Models, chat completions | Bearer API key |
| `cohere` | Cohere | Chat, embeddings | Bearer API key |
| `cloudflare` | Cloudflare | Verify token, zones, list/create DNS record | Bearer API token |
| `vercel` | Vercel | Profile, projects, deployments | Bearer API token |
| `digitalocean` | DigitalOcean | Account, droplets, projects | Bearer API token |
| `supabase` | Supabase Management | Projects, organizations, project details | Bearer management access token, not an anon/service-role project key |
| `hackernews` | Hacker News | Top stories, item | No credentials |

## Instance-Specific Endpoints

These are **trusted server settings**, never accepted from action input or stored credentials. HTTPS on port 443, public DNS/IP destinations, no redirects, and base-path confinement still apply. One base URI is configured per provider per runtime, not per tenant. Do not share a runtime across tenants requiring different provider instances without adding separately identified provider definitions or separate runtimes.

```csharp
options.ProviderBaseUris["jira"] = new Uri("https://your-company.atlassian.net/");
options.ProviderBaseUris["confluence"] = new Uri("https://your-company.atlassian.net/");
options.ProviderBaseUris["salesforce"] = new Uri("https://your-company.my.salesforce.com/");
options.ProviderBaseUris["zendesk"] = new Uri("https://your-company.zendesk.com/");
options.ProviderBaseUris["shopify"] = new Uri("https://your-store.myshopify.com/");
```

Jira OAuth 3LO calls instead require `https://api.atlassian.com/ex/jira/CLOUD_ID/`. Confluence 3LO uses `https://api.atlassian.com/ex/confluence/CLOUD_ID/`. Configure the corresponding OAuth endpoints explicitly in `OAuthClientOptions.Endpoints` and the required scopes. Salesforce's API instance must be configured; the runtime does not trust an arbitrary `instance_url` returned by a token response. Regional endpoints and sovereign-cloud deployments require explicit trusted configuration and separate verification; commercial defaults are not a compliance certification.

See the [Jira and Confluence OAuth sample](../samples/AgentConnector.Server/ATLASSIAN.md) for Atlassian app registration, minimal scopes, popup sign-in, and agent requests using these API roots.

## Operation Limits

Actions return provider-native JSON, including pagination cursors/links. There is no automatic pagination, binary file handling, SSE streaming, retry loop, batch orchestration, provider webhook ingestion, or provider-specific SDK object model. Never follow a provider's `nextLink` as an arbitrary URL through another HTTP client with these credentials. Use the declared query parameters or a separately authorized, origin-confined proxy request.

GraphQL actions grant arbitrary queries and mutations on that provider subject to provider permissions. SQL/query actions may also mutate data or incur charges. Their operation classification is deliberately conservative (`write`); an action grant is not a query-language sandbox.

AWS services requiring SigV4, Azure resource management, Google service-account JWT exchange, GitHub App installation signing, OAuth1 services, and additional catalog providers need dedicated implementations. They are not represented as supported here merely because a generic HTTP transport exists.
