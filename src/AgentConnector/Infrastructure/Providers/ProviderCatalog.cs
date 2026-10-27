using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace AgentConnector;

public sealed class ProviderCatalog
{
    private readonly FrozenDictionary<string, ConnectorProvider> providers;
    private readonly FrozenDictionary<string, (ConnectorProvider Provider, ConnectorAction Action)> actions;
    private readonly IReadOnlyCollection<ConnectorAction> actionList;

    public ProviderCatalog(ConnectorOptions options, IEnumerable<ConnectorProvider>? additionalProviders = null)
    {
        var definitions = BuiltInProviders.Create().Concat(additionalProviders ?? []).Select(provider =>
            options.ProviderBaseUris.TryGetValue(provider.Id, out var baseUri) ? provider with { BaseUri = baseUri } : provider).ToArray();
        foreach (var provider in definitions)
        {
            NetworkGuard.ValidateHttpsUri(provider.BaseUri);
            if (provider.BaseUri.Query.Length != 0 || !provider.BaseUri.AbsolutePath.EndsWith('/'))
                throw new ArgumentException($"Provider {provider.Id} requires a base URI ending in '/' without a query.");
            if (provider.Actions.FirstOrDefault(action => !action.Id.StartsWith(provider.Id + ".", StringComparison.Ordinal)) is { } invalidAction)
            {
                throw new ArgumentException($"Action {invalidAction.Id} must start with its provider ID.");
            }
        }
        providers = definitions.ToFrozenDictionary(provider => provider.Id, StringComparer.Ordinal);
        actions = definitions.SelectMany(provider => provider.Actions.Select(action => (Provider: provider, Action: action)))
            .ToFrozenDictionary(entry => entry.Action.Id, StringComparer.Ordinal);
        actionList = Array.AsReadOnly(actions.Values.Select(entry => entry.Action).ToArray());
    }

    public IReadOnlyCollection<ConnectorProvider> Providers => providers.Values;
    public IReadOnlyCollection<ConnectorAction> Actions => actionList;
    public ConnectorProvider GetProvider(string id) => providers.TryGetValue(id, out var provider) ? provider :
        throw new ConnectorException("unknown_provider", "Provider was not found.", System.Net.HttpStatusCode.NotFound);
    public (ConnectorProvider Provider, ConnectorAction Action) GetAction(string id) => actions.TryGetValue(id, out var action) ? action :
        throw new ConnectorException("unknown_action", "Action was not found.", System.Net.HttpStatusCode.NotFound);
}

internal static class BuiltInProviders
{
    private static ActionParameter Path(string name) => new(name, ParameterLocation.Path, true);
    private static ActionParameter Query(string name, bool required = false, ParameterKind kind = ParameterKind.String) => new(name, Required: required, Kind: kind);
    private static ConnectorAction Get(string id, string path, params ActionParameter[] parameters) =>
        new(id, id[(id.IndexOf('.') + 1)..].Replace('_', ' '), HttpMethod.Get, path, parameters);
    private static ConnectorAction Post(string id, string path, params ActionParameter[] parameters) =>
        new(id, id[(id.IndexOf('.') + 1)..].Replace('_', ' '), HttpMethod.Post, path, parameters, true, true);
    private static OAuthEndpoints OAuth(string authorization, string token, bool pkce = true, bool basic = false,
        string separator = " ", IReadOnlyDictionary<string, string>? parameters = null) =>
        new(new(authorization), new(token), pkce, basic, separator, parameters);
    private static ConnectorProvider Provider(string id, string name, string url, ConnectorAction[] actions,
        CredentialKind kind = CredentialKind.Bearer, string? header = null, string? query = null,
        Dictionary<string, string>? headers = null, OAuthEndpoints? oauth = null, bool instance = false) =>
        new(id, name, new(url), kind, actions, header, query, headers, oauth, instance);

    [SuppressMessage("Minor Code Smell", "S1075", Justification = "This catalog defines the canonical public provider endpoints. Trusted hosts can override API and OAuth endpoints through ConnectorOptions.")]
    public static IEnumerable<ConnectorProvider> Create()
    {
        var google = OAuth("https://accounts.google.com/o/oauth2/v2/auth", "https://oauth2.googleapis.com/token",
            parameters: new Dictionary<string, string> { ["access_type"] = "offline" });
        var microsoft = OAuth("https://login.microsoftonline.com/common/oauth2/v2.0/authorize", "https://login.microsoftonline.com/common/oauth2/v2.0/token");
        yield return Provider("github", "GitHub", "https://api.github.com/", [
            Get("github.get_current_user", "user"),
            Get("github.list_repositories", "user/repos", Query("per_page", kind: ParameterKind.Integer), Query("page", kind: ParameterKind.Integer)),
            Get("github.get_repository", "repos/{owner}/{repo}", Path("owner"), Path("repo")),
            Get("github.list_issues", "repos/{owner}/{repo}/issues", Path("owner"), Path("repo"), Query("state"), Query("page", kind: ParameterKind.Integer)),
            Post("github.create_issue", "repos/{owner}/{repo}/issues", Path("owner"), Path("repo")),
            Post("github.create_issue_comment", "repos/{owner}/{repo}/issues/{issue_number}/comments", Path("owner"), Path("repo"), Path("issue_number")),
            Get("github.list_pull_requests", "repos/{owner}/{repo}/pulls", Path("owner"), Path("repo"), Query("state")),
            Get("github.search_repositories", "search/repositories", Query("q", true))
        ], headers: new() { ["Accept"] = "application/vnd.github+json", ["X-GitHub-Api-Version"] = "2022-11-28" },
            oauth: OAuth("https://github.com/login/oauth/authorize", "https://github.com/login/oauth/access_token"));
        yield return Provider("gitlab", "GitLab", "https://gitlab.com/api/v4/", [
            Get("gitlab.get_current_user", "user"), Get("gitlab.list_projects", "projects", Query("membership", kind: ParameterKind.Boolean)),
            Get("gitlab.list_issues", "projects/{project_id}/issues", Path("project_id")),
            Post("gitlab.create_issue", "projects/{project_id}/issues", Path("project_id")),
            Get("gitlab.list_merge_requests", "projects/{project_id}/merge_requests", Path("project_id"))
        ], oauth: OAuth("https://gitlab.com/oauth/authorize", "https://gitlab.com/oauth/token"));
        yield return Provider("gmail", "Gmail", "https://gmail.googleapis.com/gmail/v1/", [
            Get("gmail.get_profile", "users/me/profile"), Get("gmail.list_messages", "users/me/messages", Query("q"), Query("maxResults", kind: ParameterKind.Integer), Query("pageToken")),
            Get("gmail.get_message", "users/me/messages/{message_id}", Path("message_id"), Query("format")),
            Post("gmail.send_message", "users/me/messages/send"), Get("gmail.list_labels", "users/me/labels")
        ], oauth: google);
        yield return Provider("google_drive", "Google Drive", "https://www.googleapis.com/drive/v3/", [
            Get("google_drive.list_files", "files", Query("q"), Query("pageToken"), Query("pageSize", kind: ParameterKind.Integer), Query("fields")),
            Get("google_drive.get_file", "files/{file_id}", Path("file_id"), Query("fields")),
            Post("google_drive.create_file_metadata", "files"), Get("google_drive.list_permissions", "files/{file_id}/permissions", Path("file_id"))
        ], oauth: google);
        yield return Provider("google_calendar", "Google Calendar", "https://www.googleapis.com/calendar/v3/", [
            Get("google_calendar.list_calendars", "users/me/calendarList"),
            Get("google_calendar.list_events", "calendars/{calendar_id}/events", Path("calendar_id"), Query("timeMin"), Query("timeMax"), Query("pageToken")),
            Post("google_calendar.create_event", "calendars/{calendar_id}/events", Path("calendar_id"))
        ], oauth: google);
        yield return Provider("google_sheets", "Google Sheets", "https://sheets.googleapis.com/v4/", [
            Get("google_sheets.get_spreadsheet", "spreadsheets/{spreadsheet_id}", Path("spreadsheet_id")),
            Get("google_sheets.get_values", "spreadsheets/{spreadsheet_id}/values/{range}", Path("spreadsheet_id"), Path("range")),
            Post("google_sheets.batch_update", "spreadsheets/{spreadsheet_id}:batchUpdate", Path("spreadsheet_id"))
        ], oauth: google);
        yield return Provider("google_docs", "Google Docs", "https://docs.googleapis.com/v1/", [
            Get("google_docs.get_document", "documents/{document_id}", Path("document_id")),
            Post("google_docs.create_document", "documents"), Post("google_docs.batch_update", "documents/{document_id}:batchUpdate", Path("document_id"))
        ], oauth: google);
        yield return Provider("google_contacts", "Google Contacts", "https://people.googleapis.com/v1/", [
            Get("google_contacts.list_contacts", "people/me/connections", Query("personFields", true), Query("pageToken")),
            Post("google_contacts.create_contact", "people:createContact")
        ], oauth: google);
        yield return Provider("google_tasks", "Google Tasks", "https://tasks.googleapis.com/tasks/v1/", [
            Get("google_tasks.list_task_lists", "users/@me/lists"), Get("google_tasks.list_tasks", "lists/{task_list_id}/tasks", Path("task_list_id")),
            Post("google_tasks.create_task", "lists/{task_list_id}/tasks", Path("task_list_id"))
        ], oauth: google);
        yield return Provider("bigquery", "Google BigQuery", "https://bigquery.googleapis.com/bigquery/v2/", [
            Get("bigquery.list_datasets", "projects/{project_id}/datasets", Path("project_id")),
            Post("bigquery.query", "projects/{project_id}/queries", Path("project_id"))
        ], oauth: google);
        yield return Provider("google_analytics", "Google Analytics", "https://analyticsdata.googleapis.com/v1beta/", [
            Post("google_analytics.run_report", "properties/{property_id}:runReport", Path("property_id"))
        ], oauth: google);
        yield return Provider("outlook", "Microsoft Outlook", "https://graph.microsoft.com/v1.0/", [
            Get("outlook.get_profile", "me"), Get("outlook.list_messages", "me/messages", Query("$top", kind: ParameterKind.Integer), Query("$filter"), Query("$select"), Query("$skip", kind: ParameterKind.Integer)),
            Get("outlook.get_message", "me/messages/{message_id}", Path("message_id")), Post("outlook.send_mail", "me/sendMail"),
            Get("outlook.list_events", "me/events"), Post("outlook.create_event", "me/events")
        ], oauth: microsoft);
        yield return Provider("onedrive", "Microsoft OneDrive", "https://graph.microsoft.com/v1.0/", [
            Get("onedrive.list_files", "me/drive/root/children"), Get("onedrive.get_item", "me/drive/items/{item_id}", Path("item_id")),
            Post("onedrive.create_folder", "me/drive/items/{parent_id}/children", Path("parent_id"))
        ], oauth: microsoft);
        yield return Provider("teams", "Microsoft Teams", "https://graph.microsoft.com/v1.0/", [
            Get("teams.list_teams", "me/joinedTeams"), Get("teams.list_channels", "teams/{team_id}/channels", Path("team_id")),
            Post("teams.send_message", "teams/{team_id}/channels/{channel_id}/messages", Path("team_id"), Path("channel_id"))
        ], oauth: microsoft);
        yield return Provider("sharepoint", "Microsoft SharePoint", "https://graph.microsoft.com/v1.0/", [
            Get("sharepoint.search_sites", "sites", Query("search", true)),
            Get("sharepoint.list_lists", "sites/{site_id}/lists", Path("site_id")),
            Get("sharepoint.list_items", "sites/{site_id}/lists/{list_id}/items", Path("site_id"), Path("list_id"), Query("$expand"))
        ], oauth: microsoft);
        yield return Provider("slack", "Slack", "https://slack.com/api/", [
            Get("slack.auth_test", "auth.test"), Get("slack.list_channels", "conversations.list", Query("cursor"), Query("limit", kind: ParameterKind.Integer)),
            Get("slack.list_messages", "conversations.history", Query("channel", true), Query("cursor")),
            Post("slack.send_message", "chat.postMessage"), Get("slack.list_users", "users.list", Query("cursor"))
        ], oauth: OAuth("https://slack.com/oauth/v2/authorize", "https://slack.com/api/oauth.v2.access", false, separator: ","));
        yield return Provider("discord", "Discord", "https://discord.com/api/v10/", [
            Get("discord.get_current_user", "users/@me"), Get("discord.list_guilds", "users/@me/guilds"),
            Get("discord.list_messages", "channels/{channel_id}/messages", Path("channel_id"), Query("limit", kind: ParameterKind.Integer)),
            Post("discord.send_message", "channels/{channel_id}/messages", Path("channel_id"))
        ], CredentialKind.ApiKey, "Authorization");
        yield return Provider("notion", "Notion", "https://api.notion.com/v1/", [
            Get("notion.get_current_user", "users/me"), Post("notion.search", "search"),
            Get("notion.get_page", "pages/{page_id}", Path("page_id")), Post("notion.create_page", "pages"),
            Get("notion.list_block_children", "blocks/{block_id}/children", Path("block_id"), Query("start_cursor"))
        ], headers: new() { ["Notion-Version"] = "2025-09-03" },
            oauth: OAuth("https://api.notion.com/v1/oauth/authorize", "https://api.notion.com/v1/oauth/token", false, true,
                parameters: new Dictionary<string, string> { ["owner"] = "user" }) with { UseJsonTokenBody = true });
        yield return Provider("linear", "Linear", "https://api.linear.app/", [Post("linear.graphql", "graphql")], CredentialKind.ApiKey, "Authorization",
            oauth: OAuth("https://linear.app/oauth/authorize", "https://api.linear.app/oauth/token"));
        yield return Provider("jira", "Atlassian Jira", "https://api.atlassian.com/", [
            Get("jira.get_current_user", "rest/api/3/myself"), Get("jira.list_projects", "rest/api/3/project/search", Query("startAt", kind: ParameterKind.Integer)),
            Get("jira.get_issue", "rest/api/3/issue/{issue_id}", Path("issue_id")), Post("jira.create_issue", "rest/api/3/issue")
        ], instance: true);
        yield return Provider("confluence", "Atlassian Confluence", "https://api.atlassian.com/", [
            Get("confluence.list_spaces", "wiki/api/v2/spaces", Query("cursor")),
            Get("confluence.get_page", "wiki/api/v2/pages/{page_id}", Path("page_id")), Post("confluence.create_page", "wiki/api/v2/pages")
        ], instance: true);
        yield return Provider("airtable", "Airtable", "https://api.airtable.com/v0/", [
            Get("airtable.list_bases", "meta/bases", Query("offset")),
            Get("airtable.list_records", "{base_id}/{table_id}", Path("base_id"), Path("table_id"), Query("offset"), Query("filterByFormula")),
            Post("airtable.create_records", "{base_id}/{table_id}", Path("base_id"), Path("table_id"))
        ], oauth: OAuth("https://airtable.com/oauth2/v1/authorize", "https://airtable.com/oauth2/v1/token", true, true));
        yield return Provider("asana", "Asana", "https://app.asana.com/api/1.0/", [
            Get("asana.get_current_user", "users/me"), Get("asana.list_workspaces", "workspaces"),
            Get("asana.list_tasks", "tasks", Query("project"), Query("workspace"), Query("assignee"), Query("offset")), Post("asana.create_task", "tasks")
        ], oauth: OAuth("https://app.asana.com/-/oauth_authorize", "https://app.asana.com/-/oauth_token"));
        yield return Provider("clickup", "ClickUp", "https://api.clickup.com/api/v2/", [
            Get("clickup.get_current_user", "user"), Get("clickup.list_workspaces", "team"),
            Get("clickup.list_tasks", "list/{list_id}/task", Path("list_id")), Post("clickup.create_task", "list/{list_id}/task", Path("list_id"))
        ], CredentialKind.ApiKey, "Authorization");
        yield return Provider("monday", "monday.com", "https://api.monday.com/", [Post("monday.graphql", "v2")],
            CredentialKind.ApiKey, "Authorization", headers: new() { ["API-Version"] = "2026-01" });
        yield return Provider("hubspot", "HubSpot", "https://api.hubapi.com/", [
            Get("hubspot.list_contacts", "crm/v3/objects/contacts", Query("after"), Query("limit", kind: ParameterKind.Integer)),
            Post("hubspot.create_contact", "crm/v3/objects/contacts"), Get("hubspot.list_companies", "crm/v3/objects/companies", Query("after")),
            Get("hubspot.list_deals", "crm/v3/objects/deals", Query("after")), Post("hubspot.create_deal", "crm/v3/objects/deals")
        ], oauth: OAuth("https://app.hubspot.com/oauth/authorize", "https://api.hubapi.com/oauth/v1/token", false));
        yield return Provider("salesforce", "Salesforce", "https://login.salesforce.com/", [
            Get("salesforce.query", "services/data/v64.0/query", Query("q", true)),
            Get("salesforce.get_record", "services/data/v64.0/sobjects/{object_type}/{record_id}", Path("object_type"), Path("record_id")),
            Post("salesforce.create_record", "services/data/v64.0/sobjects/{object_type}", Path("object_type"))
        ], oauth: OAuth("https://login.salesforce.com/services/oauth2/authorize", "https://login.salesforce.com/services/oauth2/token"), instance: true);
        yield return Provider("zendesk", "Zendesk", "https://zendesk.com/", [
            Get("zendesk.get_current_user", "api/v2/users/me.json"), Get("zendesk.list_tickets", "api/v2/tickets.json", Query("page", kind: ParameterKind.Integer)),
            Get("zendesk.get_ticket", "api/v2/tickets/{ticket_id}.json", Path("ticket_id")), Post("zendesk.create_ticket", "api/v2/tickets.json")
        ], instance: true);
        yield return Provider("intercom", "Intercom", "https://api.intercom.io/", [
            Get("intercom.get_current_user", "me"), Get("intercom.list_contacts", "contacts", Query("starting_after")),
            Post("intercom.create_contact", "contacts"), Post("intercom.search_conversations", "conversations/search")
        ], headers: new() { ["Intercom-Version"] = "2.14" });
        yield return Provider("stripe", "Stripe", "https://api.stripe.com/v1/", [
            Get("stripe.get_account", "account"), Get("stripe.list_customers", "customers", Query("limit", kind: ParameterKind.Integer), Query("starting_after")),
            Get("stripe.list_payment_intents", "payment_intents", Query("limit", kind: ParameterKind.Integer)),
            Get("stripe.list_subscriptions", "subscriptions", Query("customer"), Query("starting_after"))
        ]);
        yield return Provider("shopify", "Shopify", "https://shopify.com/", [Post("shopify.graphql", "admin/api/2026-01/graphql.json")],
            CredentialKind.ApiKey, "X-Shopify-Access-Token", instance: true);
        yield return Provider("paypal", "PayPal", "https://api-m.paypal.com/", [
            Get("paypal.get_order", "v2/checkout/orders/{order_id}", Path("order_id")), Post("paypal.create_order", "v2/checkout/orders"),
            Get("paypal.get_subscription", "v1/billing/subscriptions/{subscription_id}", Path("subscription_id"))
        ]);
        yield return Provider("dropbox", "Dropbox", "https://api.dropboxapi.com/2/", [
            Post("dropbox.get_current_account", "users/get_current_account") with { RequiresBody = false },
            Post("dropbox.list_files", "files/list_folder"), Post("dropbox.get_metadata", "files/get_metadata"), Post("dropbox.create_folder", "files/create_folder_v2")
        ], oauth: OAuth("https://www.dropbox.com/oauth2/authorize", "https://api.dropboxapi.com/oauth2/token",
            parameters: new Dictionary<string, string> { ["token_access_type"] = "offline" }));
        yield return Provider("box", "Box", "https://api.box.com/2.0/", [
            Get("box.get_current_user", "users/me"), Get("box.list_files", "folders/{folder_id}/items", Path("folder_id"), Query("marker")),
            Post("box.create_folder", "folders")
        ], oauth: OAuth("https://account.box.com/api/oauth2/authorize", "https://api.box.com/oauth2/token", false));
        yield return Provider("zoom", "Zoom", "https://api.zoom.us/v2/", [
            Get("zoom.get_current_user", "users/me"), Get("zoom.list_meetings", "users/me/meetings", Query("next_page_token")),
            Post("zoom.create_meeting", "users/me/meetings")
        ], oauth: OAuth("https://zoom.us/oauth/authorize", "https://zoom.us/oauth/token", false, true));
        yield return Provider("sendgrid", "SendGrid", "https://api.sendgrid.com/v3/", [
            Get("sendgrid.get_profile", "user/profile"), Post("sendgrid.send_email", "mail/send"), Get("sendgrid.list_templates", "templates", Query("generations"))
        ]);
        yield return Provider("twilio", "Twilio", "https://api.twilio.com/2010-04-01/", [
            Get("twilio.get_account", "Accounts/{account_sid}.json", Path("account_sid")),
            Get("twilio.list_messages", "Accounts/{account_sid}/Messages.json", Path("account_sid"), Query("PageSize", kind: ParameterKind.Integer))
        ], CredentialKind.Basic);
        yield return Provider("openai", "OpenAI", "https://api.openai.com/v1/", [
            Get("openai.list_models", "models"), Post("openai.create_response", "responses"), Post("openai.create_chat_completion", "chat/completions"), Post("openai.create_embeddings", "embeddings")
        ]);
        yield return Provider("anthropic", "Anthropic", "https://api.anthropic.com/v1/", [
            Get("anthropic.list_models", "models"), Post("anthropic.create_message", "messages")
        ], CredentialKind.ApiKey, "x-api-key", headers: new() { ["anthropic-version"] = "2023-06-01" });
        yield return Provider("gemini", "Google Gemini", "https://generativelanguage.googleapis.com/v1beta/", [
            Get("gemini.list_models", "models"), Post("gemini.generate_content", "models/{model}:generateContent", Path("model")),
            Post("gemini.embed_content", "models/{model}:embedContent", Path("model"))
        ], CredentialKind.ApiKey, "x-goog-api-key");
        yield return Provider("mistral", "Mistral AI", "https://api.mistral.ai/v1/", [
            Get("mistral.list_models", "models"), Post("mistral.create_chat_completion", "chat/completions"), Post("mistral.create_embeddings", "embeddings")
        ]);
        yield return Provider("groq", "Groq", "https://api.groq.com/openai/v1/", [
            Get("groq.list_models", "models"), Post("groq.create_chat_completion", "chat/completions")
        ]);
        yield return Provider("cohere", "Cohere", "https://api.cohere.com/v2/", [Post("cohere.chat", "chat"), Post("cohere.embed", "embed")]);
        yield return Provider("cloudflare", "Cloudflare", "https://api.cloudflare.com/client/v4/", [
            Get("cloudflare.verify_token", "user/tokens/verify"), Get("cloudflare.list_zones", "zones", Query("page", kind: ParameterKind.Integer)),
            Get("cloudflare.list_dns_records", "zones/{zone_id}/dns_records", Path("zone_id")), Post("cloudflare.create_dns_record", "zones/{zone_id}/dns_records", Path("zone_id"))
        ]);
        yield return Provider("vercel", "Vercel", "https://api.vercel.com/", [
            Get("vercel.get_current_user", "v2/user"), Get("vercel.list_projects", "v9/projects", Query("teamId")),
            Get("vercel.list_deployments", "v6/deployments", Query("projectId"), Query("teamId"))
        ]);
        yield return Provider("digitalocean", "DigitalOcean", "https://api.digitalocean.com/v2/", [
            Get("digitalocean.get_account", "account"), Get("digitalocean.list_droplets", "droplets", Query("page", kind: ParameterKind.Integer)),
            Get("digitalocean.list_projects", "projects")
        ]);
        yield return Provider("supabase", "Supabase Management", "https://api.supabase.com/v1/", [
            Get("supabase.list_projects", "projects"), Get("supabase.list_organizations", "organizations"),
            Get("supabase.get_project", "projects/{project_ref}", Path("project_ref"))
        ]);
        yield return Provider("hackernews", "Hacker News", "https://hacker-news.firebaseio.com/v0/", [
            Get("hackernews.get_top_stories", "topstories.json"), Get("hackernews.get_item", "item/{item_id}.json", Path("item_id"))
        ], CredentialKind.None);
    }
}
