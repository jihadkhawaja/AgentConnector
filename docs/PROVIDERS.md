# Provider Coverage

There are **49 adapters and 221 named actions**. These are implemented HTTP request contracts, not catalog-only entries. Automated tests build every action and exercise local security/OAuth contracts with mocked responses. **No credentialed adapter has been live-account certified.** Obtain your own API credentials, register any OAuth apps, grant minimal scopes, and verify your required operations in a provider sandbox before release.

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
| `teams` | [Microsoft Teams](#microsoft-teams) | Teams, members, channels, chats, message search, replies, online meetings | Microsoft Graph OAuth / bearer |
| `dynamics365` | [Microsoft Dynamics 365](#microsoft-dynamics-365) | Dataverse table metadata, record search and CRUD | Microsoft Entra OAuth / bearer; trusted environment URI required |
| `sharepoint` | Microsoft SharePoint | Site search, lists, items | Microsoft Graph OAuth / bearer |
| `slack` | Slack | Auth test, channels/history/users, send message | Bearer / bot-install OAuth; comma-separated scopes |
| `discord` | Discord | Current user, guilds, channel messages/send | `api_key` with full `Bot TOKEN` or `Bearer TOKEN` Authorization header value |
| `notion` | Notion | Current user, search, pages, block children | Bearer / OAuth; pinned Notion API version |
| `linear` | Linear | GraphQL query/mutation | `api_key` raw personal token in Authorization; OAuth uses Bearer |
| `jira` | [Atlassian Jira](#atlassian-jira) | Projects, issue search and edits, assignment, transitions, comments | Basic email/API token or bearer; trusted instance URI required |
| `confluence` | [Atlassian Confluence](#atlassian-confluence) | Content search, spaces, pages, child content, footer comments | Basic email/API token or bearer; trusted instance URI required |
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
| `twilio` | [Twilio](#twilio) | Account, SMS/MMS and template sends, call logs and controls, owned numbers | Basic account SID + auth token, or an API key and secret with the required permissions |
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
options.ProviderBaseUris["dynamics365"] = new Uri("https://your-org.crm.dynamics.com/");
options.ProviderBaseUris["salesforce"] = new Uri("https://your-company.my.salesforce.com/");
options.ProviderBaseUris["zendesk"] = new Uri("https://your-company.zendesk.com/");
options.ProviderBaseUris["shopify"] = new Uri("https://your-store.myshopify.com/");
```

Jira OAuth 3LO calls instead require `https://api.atlassian.com/ex/jira/CLOUD_ID/`. Confluence 3LO uses `https://api.atlassian.com/ex/confluence/CLOUD_ID/`. Configure the corresponding OAuth endpoints explicitly in `OAuthClientOptions.Endpoints` and the required scopes. Salesforce's API instance must be configured; the runtime does not trust an arbitrary `instance_url` returned by a token response. Regional endpoints and sovereign-cloud deployments require explicit trusted configuration and separate verification; commercial defaults are not a compliance certification.

See the [Jira and Confluence OAuth sample](../samples/AgentConnector.Server/ATLASSIAN.md) for Atlassian app registration, minimal scopes, popup sign-in, and agent requests using these API roots.

## Action reference

Add each needed ID to `ConnectorOptions.AllowedActions`. Call it with `POST /connector/v1/actions/{actionId}` and a JSON object containing `input` and, when needed, `connectionName`. The tables below list the built-in actions for these five providers. Use `GET /connector/v1/actions?service={providerId}` for the full input schema of each action your host has allowed.

### Atlassian Jira

| Action IDs | What they do |
| --- | --- |
| `jira.get_current_user` | Read the connected Jira profile. |
| `jira.list_projects`, `jira.get_project` | Find accessible projects and look up one by ID or key. |
| `jira.list_issue_types`, `jira.list_create_issue_fields`, `jira.list_edit_issue_fields` | Discover creatable issue types and fields for a project and editable fields on an existing issue. |
| `jira.search_issues` | Run JQL against Jira Cloud's enhanced search. |
| `jira.get_issue`, `jira.create_issue`, `jira.update_issue`, `jira.delete_issue` | Read, create, edit, or delete an issue. Set `deleteSubtasks` to `true` to also delete its subtasks. |
| `jira.find_assignable_users`, `jira.assign_issue` | Look up eligible users for an issue key and assign the issue by account ID. |
| `jira.list_issue_transitions`, `jira.transition_issue` | Find the issue's available transitions and apply one by ID. |
| `jira.list_issue_comments`, `jira.get_issue_comment`, `jira.add_issue_comment`, `jira.update_issue_comment`, `jira.delete_issue_comment` | Read and manage issue comments. |

For `jira.search_issues`, use `{"input":{"jql":"project = DEMO ORDER BY updated DESC","fields":"summary,status","maxResults":25}}`. Include the returned `nextPageToken` in the next input. Request `fields` to retrieve the issue details you need. Jira Cloud v3 uses Atlassian Document Format for issue descriptions and comment bodies. For example, send `{"input":{"issue_id":"DEMO-1","body":{"transition":{"id":"31"}}}}` to `jira.transition_issue` after looking up the available transition ID.

For OAuth, the classic scopes include `read:jira-user` for the profile and user lookup, `read:jira-work` for issue/project reads and search, and `write:jira-work` for issue writes. Jira also checks project permissions and issue visibility. [Search API](https://developer.atlassian.com/cloud/jira/platform/rest/v3/api-group-issue-search/), [issue API](https://developer.atlassian.com/cloud/jira/platform/rest/v3/api-group-issues/), [comment API](https://developer.atlassian.com/cloud/jira/platform/rest/v3/api-group-issue-comments/).

### Atlassian Confluence

| Action IDs | What they do |
| --- | --- |
| `confluence.search` | Find content with Confluence Query Language (CQL). |
| `confluence.list_spaces`, `confluence.get_space` | List accessible spaces and look up one by numeric ID. |
| `confluence.list_pages`, `confluence.get_page`, `confluence.list_page_children` | List pages, read page content, and list direct children. |
| `confluence.create_page`, `confluence.update_page`, `confluence.delete_page` | Create, revise, or trash a page. |
| `confluence.list_page_comments`, `confluence.get_page_comment`, `confluence.add_page_comment`, `confluence.update_page_comment`, `confluence.delete_page_comment` | Read and manage footer comments. |

For `confluence.search`, use `{"input":{"cql":"type = page AND space = DEMO","limit":25}}`. Use the cursor from the returned next link for the next request. For `confluence.get_page`, use `{"input":{"page_id":"123456","body-format":"storage"}}` to request the storage representation. To add a footer comment, supply `pageId` and a Confluence comment body inside `input.body`. Page and comment updates use the provider's versioned payload. Fetch the current version before editing and submit the next version number.

For OAuth, CQL needs `search:confluence` (classic) or `read:content-details:confluence` (granular). Use the separate granular scopes for spaces (`read:space:confluence`), pages (`read:page:confluence`), child content (`read:hierarchical-content:confluence`), and comments (`read:comment:confluence`). Grant the corresponding `write:` or `delete:` page or comment scopes only for enabled writes. Confluence checks content visibility and permissions for the connected user. [CQL search API](https://developer.atlassian.com/cloud/confluence/rest/v1/api-group-search/), [page API](https://developer.atlassian.com/cloud/confluence/rest/v2/api-group-page/), [comment API](https://developer.atlassian.com/cloud/confluence/rest/v2/api-group-comment/).

### Microsoft Teams

| Action IDs | What they do |
| --- | --- |
| `teams.list_teams`, `teams.get_team`, `teams.list_team_members` | List joined teams and read team details and membership. |
| `teams.list_channels`, `teams.get_channel`, `teams.create_channel`, `teams.list_channel_members` | Find, read, or create a team's channels and list a channel's direct members. |
| `teams.list_channel_messages`, `teams.get_channel_message`, `teams.send_message` | Read or post top-level channel messages. |
| `teams.list_channel_replies`, `teams.get_channel_reply`, `teams.reply_to_message` | Read or reply to a channel thread. |
| `teams.list_chats`, `teams.get_chat`, `teams.create_chat`, `teams.list_chat_members` | Find, read, or create chats and list their members. |
| `teams.list_chat_messages`, `teams.get_chat_message`, `teams.send_chat_message` | Read and send chat messages. |
| `teams.search_messages` | Search Teams messages accessible to the signed-in user. |
| `teams.create_online_meeting`, `teams.get_online_meeting` | Create or read a standalone Teams meeting for the signed-in user. |

Use a Microsoft Graph work or school connection for delegated actions. Common delegated permissions include `Team.ReadBasic.All` for team discovery, `TeamMember.Read.All` and `ChannelMember.Read.All` for membership, `Channel.ReadBasic.All` and `Channel.Create` for channel metadata and creation, `ChannelMessage.Read.All` and `ChannelMessage.Send` for channel messages, and `Chat.Read`, `Chat.Create`, and `ChatMessage.Send` for reading, creating, and messaging chats. The meeting actions use `OnlineMeetings.Read` or `OnlineMeetings.ReadWrite` and create standalone meetings without calendar invitations. Check each Graph endpoint for its supported delegated and application permission modes. [Microsoft Graph permissions](https://learn.microsoft.com/en-us/graph/permissions-reference), [online meeting API](https://learn.microsoft.com/en-us/graph/api/application-post-onlinemeetings?view=graph-rest-1.0).

For `teams.search_messages`, supply one `chatMessage` search request in `input.body`. Set `from` and `size` to page through results. Graph lists `Chat.Read`, `Chat.ReadWrite`, and `ChannelMessage.Read.All` as delegated scopes for Teams message search. [Search API](https://learn.microsoft.com/en-us/graph/search-concept-chat-messages), [search permissions](https://learn.microsoft.com/en-us/graph/api/resources/search-api-overview?view=graph-rest-1.0).

```json
{"input":{"body":{"requests":[{"entityTypes":["chatMessage"],"query":{"queryString":"release notes"},"from":0,"size":25}]}}}
```

To send or reply, use the relevant team/channel/thread or chat IDs as top-level inputs and set `input.body` to `{"body":{"contentType":"text","content":"Release notes are ready."}}`. [Graph messaging API](https://learn.microsoft.com/en-us/graph/api/chatmessage-post?view=graph-rest-1.0).

### Twilio

| Action IDs | What they do |
| --- | --- |
| `twilio.get_account` | Read account metadata. |
| `twilio.list_messages`, `twilio.get_message` | Search message logs by sender, recipient, or date and look up a message by SID. |
| `twilio.send_message` | Send SMS, MMS, or a Twilio content template. |
| `twilio.list_calls`, `twilio.get_call` | Search call logs by sender, recipient, status, or time and look up a call by SID. |
| `twilio.create_call`, `twilio.update_call_status` | Place a voice call with inline TwiML, cancel a queued call, or end a call in progress. |
| `twilio.list_phone_numbers`, `twilio.get_phone_number` | Find or look up numbers provisioned to the account. |

Set the connection kind to `basic` and provide `account_sid` for every action. List actions accept `PageSize` and `PageToken`. For sends and call writes, pass the form fields directly under `input` using Twilio's capitalization. AgentConnector encodes them as `application/x-www-form-urlencoded`.

#### Send an SMS

Call `POST /connector/v1/actions/twilio.send_message`. Replace `account_sid` with your Twilio Account SID, `To` with the recipient's number, and `From` with your SMS-enabled Twilio number. [Twilio SMS API](https://www.twilio.com/docs/messaging/api/message-resource#send-an-sms-message).

```json
{
  "input": {
    "account_sid": "AC00000000000000000000000000000000",
    "To": "+12025550101",
    "From": "+12025550102",
    "Body": "Your appointment is confirmed."
  }
}
```

Use `MessagingServiceSid` if the service selects the sender. MMS can use one `MediaUrl`. Templates use `ContentSid` and optional `ContentVariables` as a JSON-encoded string. [Twilio Message API](https://www.twilio.com/docs/messaging/api/message-resource).

#### Call a phone number

Call `POST /connector/v1/actions/twilio.create_call`. Use the destination number in `To`, a voice-capable Twilio number or verified caller ID in `From`, and inline `Twiml` for what Twilio should do when the call connects. This example speaks a short message. [Twilio Call API](https://www.twilio.com/docs/voice/api/call-resource).

```json
{
  "input": {
    "account_sid": "AC00000000000000000000000000000000",
    "To": "+12025550101",
    "From": "+12025550102",
    "Twiml": "<Response><Say>Your appointment is confirmed.</Say></Response>"
  }
}
```

To check the call, pass its returned SID as `call_sid` to `twilio.get_call`. To change its status, pass `call_sid` and a `Status` of `canceled` or `completed` to `twilio.update_call_status`. [Twilio Call API](https://www.twilio.com/docs/voice/api/call-resource).

### Microsoft Dynamics 365

| Action IDs | What they do |
| --- | --- |
| `dynamics365.who_am_i` | Verify the connected user and environment. |
| `dynamics365.list_tables` | Inspect Dataverse table definitions. |
| `dynamics365.list_records`, `dynamics365.get_record` | Filter rows with OData or fetch one row by GUID. |
| `dynamics365.search_records` | Run full-text Dataverse search over indexed tables. |
| `dynamics365.create_record`, `dynamics365.update_record`, `dynamics365.delete_record` | Create, patch, or delete a row. Create returns the row representation, and patch requires an existing row. |

This adapter targets Dynamics 365 apps backed by Dataverse, such as Sales and Customer Service. Set the trusted environment root (for example, `https://your-org.crm.dynamics.com/`) in `ProviderBaseUris["dynamics365"]`. For delegated OAuth, request the Dataverse scope `https://your-org.crm.dynamics.com/user_impersonation` and `offline_access` if refresh is needed. For a manually stored application bearer token, acquire it for that Dataverse environment and configure the app as a Dataverse application user with the appropriate security roles. [Dataverse OAuth](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/authenticate-oauth).

The supported entity sets for row actions are `accounts`, `contacts`, `leads`, `opportunities`, `incidents`, `tasks`, `appointments`, `phonecalls`, `emails`, and `annotations`, subject to environment availability. For example, use `{"input":{"entity_set":"accounts","$select":"accountid,name","$filter":"startswith(name,'Contoso')","$top":10}}` with `dynamics365.list_records`. Row writes take Dataverse field names under `input.body`. [Dataverse OData queries](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/query/overview), [row updates and deletion](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/update-delete-table-rows).

Dataverse search must be enabled in the environment. Use `{"input":{"body":{"search":"Contoso","top":10}}}` with `dynamics365.search_records`. The result retains Dataverse's native `response` field, which contains serialized JSON. [Dataverse search API](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/search/query).

## Operation Limits

Actions return provider-native JSON, including pagination cursors/links. There is no automatic pagination, binary file handling, SSE streaming, retry loop, batch orchestration, provider webhook ingestion, or provider-specific SDK object model. Never follow a provider's `nextLink` as an arbitrary URL through another HTTP client with these credentials. Use the declared query parameters or a separately authorized, origin-confined proxy request.

GraphQL actions grant arbitrary queries and mutations on that provider subject to provider permissions. SQL/query actions may also mutate data or incur charges. Their operation classification is deliberately conservative (`write`); an action grant is not a query-language sandbox.

AWS services requiring SigV4, Azure resource management, Google service-account JWT exchange, GitHub App installation signing, OAuth1 services, and additional catalog providers need dedicated implementations. They are not represented as supported here merely because a generic HTTP transport exists.
