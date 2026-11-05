using System.Text.Json.Nodes;

namespace AgentConnector;

internal static partial class BuiltInProviders
{
    private static ActionParameter Number(string name) => Query(name, kind: ParameterKind.Integer);
    private static ActionParameter Form(string name, bool required = false) => new(name, ParameterLocation.Form, required);

    private static ConnectorAction[] JiraActions()
    {
        const string api = "rest/api/3/";
        const string issue = api + "issue/{issue_id}";
        const string createTypes = api + "issue/createmeta/{project_id}/issuetypes";
        var issueId = Path("issue_id");
        var projectId = Path("project_id");
        var commentId = Path("comment_id");
        return [
            Get("jira.get_current_user", api + "myself"),
            Get("jira.list_projects", api + "project/search", Number("startAt"), Number("maxResults"), Query("query")),
            Get("jira.get_project", api + "project/{project_id}", projectId),
            Get("jira.list_issue_types", createTypes, projectId, Number("startAt"), Number("maxResults")),
            Get("jira.list_create_issue_fields", createTypes + "/{issue_type_id}", projectId, Path("issue_type_id"), Number("startAt"), Number("maxResults")),
            Get("jira.search_issues", api + "search/jql", Query("jql", true), Query("nextPageToken"), Number("maxResults"), Query("fields"), Query("expand")),
            Get("jira.get_issue", issue, issueId, Query("fields"), Query("expand")),
            Post("jira.create_issue", api + "issue"),
            Get("jira.list_edit_issue_fields", issue + "/editmeta", issueId),
            JsonWrite("jira.update_issue", HttpMethod.Put, issue, issueId),
            Action("jira.delete_issue", HttpMethod.Delete, issue, issueId, Query("deleteSubtasks", kind: ParameterKind.Boolean)),
            Get("jira.find_assignable_users", api + "user/assignable/search", Query("issueKey", true), Query("query"),
                Query("accountId"), Number("startAt"), Number("maxResults")),
            JsonWrite("jira.assign_issue", HttpMethod.Put, issue + "/assignee", issueId),
            Get("jira.list_issue_transitions", issue + "/transitions", issueId, Query("expand")),
            Post("jira.transition_issue", issue + "/transitions", issueId),
            Get("jira.list_issue_comments", issue + "/comment", issueId, Number("startAt"), Number("maxResults"), Query("orderBy")),
            Get("jira.get_issue_comment", issue + "/comment/{comment_id}", issueId, commentId),
            Post("jira.add_issue_comment", issue + "/comment", issueId),
            JsonWrite("jira.update_issue_comment", HttpMethod.Put, issue + "/comment/{comment_id}", issueId, commentId),
            Action("jira.delete_issue_comment", HttpMethod.Delete, issue + "/comment/{comment_id}", issueId, commentId)
        ];
    }

    private static ConnectorAction[] ConfluenceActions()
    {
        const string api = "wiki/api/v2/";
        const string page = api + "pages/{page_id}";
        const string comment = api + "footer-comments/{comment_id}";
        var pageId = Path("page_id");
        var commentId = Path("comment_id");
        var cursor = Query("cursor");
        var limit = Number("limit");
        var bodyFormat = Query("body-format");
        return [
            Get("confluence.search", "wiki/rest/api/search", Query("cql", true), cursor, limit),
            Get("confluence.list_spaces", api + "spaces", cursor, limit, Query("keys"), Query("status")),
            Get("confluence.get_space", api + "spaces/{space_id}", Path("space_id")),
            Get("confluence.list_pages", api + "pages", Number("space-id"), Query("title"), bodyFormat, cursor, limit),
            Get("confluence.get_page", page, pageId, bodyFormat, Number("version")),
            Get("confluence.list_page_children", page + "/direct-children", pageId, cursor, limit),
            Post("confluence.create_page", api + "pages"),
            JsonWrite("confluence.update_page", HttpMethod.Put, page, pageId),
            Action("confluence.delete_page", HttpMethod.Delete, page, pageId),
            Get("confluence.list_page_comments", page + "/footer-comments", pageId, bodyFormat, cursor, limit),
            Get("confluence.get_page_comment", comment, commentId, bodyFormat),
            Post("confluence.add_page_comment", api + "footer-comments"),
            JsonWrite("confluence.update_page_comment", HttpMethod.Put, comment, commentId),
            Action("confluence.delete_page_comment", HttpMethod.Delete, comment, commentId)
        ];
    }

    private static ConnectorAction[] TeamsActions()
    {
        const string team = "teams/{team_id}";
        const string channel = team + "/channels/{channel_id}";
        const string channelMessage = channel + "/messages/{message_id}";
        const string chat = "chats/{chat_id}";
        var teamId = Path("team_id");
        var channelId = Path("channel_id");
        var chatId = Path("chat_id");
        var messageId = Path("message_id");
        var top = Number("$top");
        var skipToken = Query("$skiptoken");
        return [
            Get("teams.list_teams", "me/joinedTeams"),
            Get("teams.get_team", team, teamId),
            Get("teams.list_team_members", team + "/members", teamId, top, skipToken, Query("$filter")),
            Get("teams.list_channels", team + "/channels", teamId, Query("$filter"), Query("$select")),
            Get("teams.get_channel", channel, teamId, channelId),
            Get("teams.list_channel_members", channel + "/members", teamId, channelId, top, skipToken, Query("$filter"), Query("$select")),
            Post("teams.create_channel", team + "/channels", teamId),
            Get("teams.list_channel_messages", channel + "/messages", teamId, channelId, top, skipToken),
            Get("teams.get_channel_message", channelMessage, teamId, channelId, messageId),
            Get("teams.list_channel_replies", channelMessage + "/replies", teamId, channelId, messageId, top, skipToken),
            Get("teams.get_channel_reply", channelMessage + "/replies/{reply_id}", teamId, channelId, messageId, Path("reply_id")),
            Post("teams.send_message", channel + "/messages", teamId, channelId),
            Post("teams.reply_to_message", channelMessage + "/replies", teamId, channelId, messageId),
            Get("teams.list_chats", "chats", top, skipToken, Query("$filter"), Query("$expand"), Query("$orderby")),
            Get("teams.get_chat", chat, chatId, Query("$expand")),
            Get("teams.list_chat_members", chat + "/members", chatId),
            Post("teams.create_chat", "chats"),
            Get("teams.list_chat_messages", chat + "/messages", chatId, top, skipToken, Query("$filter"), Query("$orderby")),
            Get("teams.get_chat_message", chat + "/messages/{message_id}", chatId, messageId),
            Post("teams.send_chat_message", chat + "/messages", chatId),
            Post("teams.search_messages", "search/query") with { ReadOnlyPost = true, ValidateBody = IsTeamsSearch },
            Post("teams.create_online_meeting", "me/onlineMeetings"),
            Get("teams.get_online_meeting", "me/onlineMeetings/{meeting_id}", Path("meeting_id"))
        ];
    }

    private static bool IsTeamsSearch(JsonObject body) =>
        body.Count == 1 && body["requests"] is JsonArray { Count: 1 } requests &&
        requests[0] is JsonObject request &&
        request.All(field => field.Key is "entityTypes" or "query" or "from" or "size" or "enableTopResults") &&
        request["entityTypes"] is JsonArray { Count: 1 } types &&
        types[0] is JsonValue type && type.TryGetValue<string>(out var value) && value == "chatMessage";

    private static ConnectorAction[] TwilioActions()
    {
        const string account = "Accounts/{account_sid}";
        const string calls = account + "/Calls";
        const string messages = account + "/Messages";
        const string numbers = account + "/IncomingPhoneNumbers";
        var accountSid = Path("account_sid");
        var callSid = Path("call_sid");
        var pageSize = Number("PageSize");
        var pageToken = Query("PageToken");
        return [
            Get("twilio.get_account", account + ".json", accountSid),
            Get("twilio.list_messages", messages + ".json", accountSid, pageSize, pageToken, Query("To"), Query("From"), Query("DateSent")),
            Get("twilio.get_message", messages + "/{message_sid}.json", accountSid, Path("message_sid")),
            Action("twilio.send_message", HttpMethod.Post, messages + ".json", accountSid,
                Form("To", true), Form("From"), Form("MessagingServiceSid"), Form("Body"), Form("MediaUrl"),
                Form("ContentSid"), Form("ContentVariables")),
            Get("twilio.list_calls", calls + ".json", accountSid, pageSize, pageToken, Query("To"), Query("From"), Query("Status"), Query("StartTime"), Query("EndTime")),
            Get("twilio.get_call", calls + "/{call_sid}.json", accountSid, callSid),
            Action("twilio.create_call", HttpMethod.Post, calls + ".json", accountSid, Form("To", true), Form("From", true), Form("Twiml", true)),
            Action("twilio.update_call_status", HttpMethod.Post, calls + "/{call_sid}.json", accountSid, callSid,
                Form("Status", true) with { AllowedValues = ["canceled", "completed"] }),
            Get("twilio.list_phone_numbers", numbers + ".json", accountSid, pageSize, pageToken, Query("PhoneNumber"), Query("FriendlyName")),
            Get("twilio.get_phone_number", numbers + "/{phone_number_sid}.json", accountSid, Path("phone_number_sid"))
        ];
    }

    private static ConnectorAction[] Dynamics365Actions()
    {
        const string api = "api/data/v9.2/";
        const string records = api + "{entity_set}";
        const string record = records + "({record_id})";
        // The OData root also exposes unbound actions. Restrict row operations to known entity sets.
        var entitySet = Path("entity_set") with
        {
            AllowedValues = ["accounts", "contacts", "leads", "opportunities", "incidents", "tasks", "appointments", "phonecalls", "emails", "annotations"]
        };
        var recordId = Path("record_id") with { Kind = ParameterKind.Guid };
        return [
            Get("dynamics365.who_am_i", api + "WhoAmI"),
            Get("dynamics365.list_tables", api + "EntityDefinitions", Query("$select"), Query("$filter")),
            Get("dynamics365.list_records", records, entitySet, Query("$select"), Query("$filter"), Query("$orderby"),
                Query("$expand"), Number("$top"), Query("$skiptoken")),
            Get("dynamics365.get_record", record, entitySet, recordId, Query("$select"), Query("$expand")),
            Post("dynamics365.create_record", records, entitySet) with { Headers = new Dictionary<string, string> { ["Prefer"] = "return=representation" } },
            JsonWrite("dynamics365.update_record", HttpMethod.Patch, record, entitySet, recordId) with
            {
                Headers = new Dictionary<string, string> { ["If-Match"] = "*" }
            },
            Action("dynamics365.delete_record", HttpMethod.Delete, record, entitySet, recordId),
            Post("dynamics365.search_records", api + "searchquery") with { ReadOnlyPost = true }
        ];
    }
}
