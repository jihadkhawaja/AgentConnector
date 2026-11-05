using System.Text.Json.Nodes;
using AgentConnector;
using Microsoft.AspNetCore.WebUtilities;

namespace AgentConnector.Tests;

public sealed class RequestBuilderTests
{
    private readonly ProviderCatalog catalog = new(new());

    [Theory]
    [InlineData(CredentialKind.None, "secret", null)]
    [InlineData((CredentialKind)999, "secret", null)]
    [InlineData(CredentialKind.Bearer, null, null)]
    [InlineData(CredentialKind.Bearer, "", null)]
    [InlineData(CredentialKind.Bearer, "token with spaces", null)]
    [InlineData(CredentialKind.OAuth2, "token\twith\ttabs", null)]
    [InlineData(CredentialKind.Basic, "secret", null)]
    [InlineData(CredentialKind.Basic, "secret", "user:name")]
    [InlineData(CredentialKind.Basic, "secret", "user\nname")]
    [InlineData(CredentialKind.ApiKey, "secret", null)]
    public void CredentialValidationRejectsInvalidValues(CredentialKind kind, string? secret, string? userName)
    {
        var provider = catalog.GetProvider("github");
        var credential = new ConnectorCredential(kind, secret!, userName);

        var exception = Assert.Throws<ConnectorException>(() => credential.Validate(provider));
        Assert.Equal("invalid_input", exception.Code);
        var legacyException = Assert.Throws<ConnectorException>(() => ProviderRequestBuilder.ValidateCredential(provider, credential));
        Assert.Equal(exception.Code, legacyException.Code);
        Assert.Equal(exception.Message, legacyException.Message);
        Assert.Equal(exception.Status, legacyException.Status);
    }

    [Theory]
    [InlineData("github", CredentialKind.Bearer, null)]
    [InlineData("github", CredentialKind.OAuth2, null)]
    [InlineData("github", CredentialKind.Basic, "user")]
    [InlineData("anthropic", CredentialKind.ApiKey, null)]
    public void CredentialValidationAcceptsSupportedValues(string providerId, CredentialKind kind, string? userName)
    {
        var provider = catalog.GetProvider(providerId);
        var credential = new ConnectorCredential(kind, "secret", userName);

        credential.Validate(provider);
        ProviderRequestBuilder.ValidateCredential(provider, credential);
    }

    [Fact]
    public void CredentialValidationEnforcesSecretLengthLimit()
    {
        var provider = catalog.GetProvider("github");
        new ConnectorCredential(CredentialKind.Bearer, new string('a', 32768)).Validate(provider);
        Assert.Throws<ConnectorException>(() =>
            new ConnectorCredential(CredentialKind.Bearer, new string('a', 32769)).Validate(provider));
    }

    [Theory]
    [InlineData("username")]
    [InlineData("refresh_token")]
    [InlineData("scope_length")]
    [InlineData("scope_count")]
    [InlineData("scope_control")]
    [InlineData("scope_whitespace")]
    [InlineData("scope_null")]
    public void CredentialValidationBoundsAllSecretAndScopeFields(string field)
    {
        var credential = new ConnectorCredential(CredentialKind.OAuth2, "access-token");
        credential = field switch
        {
            "username" => credential with { UserName = new string('u', 32769) },
            "refresh_token" => credential with { RefreshToken = new string('r', 32769) },
            "scope_length" => credential with { Scopes = [new string('s', 2049)] },
            "scope_count" => credential with { Scopes = Enumerable.Repeat("read", 257).ToArray() },
            "scope_control" => credential with { Scopes = ["read\nwrite"] },
            "scope_whitespace" => credential with { Scopes = ["read write"] },
            _ => credential with { Scopes = [null!] }
        };
        Assert.Equal("invalid_input", Assert.Throws<ConnectorException>(() => credential.Validate(catalog.GetProvider("github"))).Code);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../other")]
    [InlineData("%2e%2e")]
    [InlineData("https://evil.example")]
    [InlineData("owner\\other")]
    public void RejectsPathInjection(string owner)
    {
        var (provider, action) = catalog.GetAction("github.get_repository");
        Assert.Throws<ConnectorException>(() => ProviderRequestBuilder.Build(provider, action, new() { ["owner"] = owner, ["repo"] = "example" }));
    }

    [Theory]
    [InlineData("//evil.example/path")]
    [InlineData("/../user")]
    [InlineData("/%2e%2e/user")]
    [InlineData("/path?token=leak")]
    [InlineData("/path#fragment")]
    [InlineData("/https://evil.example")]
    public void ProxyCannotEscapeOriginOrBasePath(string path)
    {
        Assert.Throws<ConnectorException>(() => ProviderRequestBuilder.BuildProxy(catalog.GetProvider("gmail"), new(path)));
    }

    [Fact]
    public void ValidatesTypesAndUnknownInputs()
    {
        var (provider, action) = catalog.GetAction("github.list_repositories");
        Assert.Throws<ConnectorException>(() => ProviderRequestBuilder.Build(provider, action, new() { ["per_page"] = "10" }));
        Assert.Throws<ConnectorException>(() => ProviderRequestBuilder.Build(provider, action, new() { ["url"] = "https://evil.example" }));
        using var request = ProviderRequestBuilder.Build(provider, action, new() { ["per_page"] = 10 });
        Assert.Equal("https://api.github.com/user/repos?per_page=10", request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task BuildsJsonWriteAndProviderSpecificAuthentication()
    {
        var (provider, action) = catalog.GetAction("anthropic.create_message");
        using var request = ProviderRequestBuilder.Build(provider, action, new() { ["body"] = new JsonObject { ["model"] = "test-model" } });
        ProviderRequestBuilder.Authenticate(request, provider, new(CredentialKind.ApiKey, "test-key"));
        Assert.Equal("test-key", request.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", request.Headers.GetValues("anthropic-version").Single());
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
        Assert.Contains("test-model", await request.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("twilio.send_message", "Messages.json", "Body", "Hello + café & friends")]
    [InlineData("twilio.create_call", "Calls.json", "Twiml", "<Response><Say>Hello!</Say></Response>")]
    public async Task BuildsTwilioFormWrites(string id, string endpoint, string field, string value)
    {
        var (provider, action) = catalog.GetAction(id);
        using var request = ProviderRequestBuilder.Build(provider, action, new()
        {
            ["account_sid"] = "AC123", ["To"] = "+15550100001", ["From"] = "+15550100002", [field] = value
        });
        ProviderRequestBuilder.Authenticate(request, provider, new(CredentialKind.Basic, "synthetic-secret", UserName: "AC123"));
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"https://api.twilio.com/2010-04-01/Accounts/AC123/{endpoint}", request.RequestUri!.AbsoluteUri);
        Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
        Assert.Equal("application/x-www-form-urlencoded", request.Content!.Headers.ContentType!.MediaType);
        var form = QueryHelpers.ParseQuery(await request.Content.ReadAsStringAsync());
        Assert.Equal("+15550100001", form["To"].ToString());
        Assert.Equal("+15550100002", form["From"].ToString());
        Assert.Equal(value, form[field].ToString());
        Assert.Equal(3, form.Count);
    }

    [Fact]
    public void TwilioFormWritesRejectMissingRequiredAndUnknownFields()
    {
        var (provider, action) = catalog.GetAction("twilio.send_message");
        Assert.Throws<ConnectorException>(() => ProviderRequestBuilder.Build(provider, action, new() { ["account_sid"] = "AC123" }));
        Assert.Throws<ConnectorException>(() => ProviderRequestBuilder.Build(provider, action, new()
        {
            ["account_sid"] = "AC123", ["To"] = "+15550100001", ["body"] = new JsonObject()
        }));
        Assert.Contains("To", ActionSchemas.Input(action)["required"]!.AsArray().Select(node => node!.GetValue<string>()));
    }

    [Theory]
    [InlineData("teams.search_messages", """{"requests":[{"entityTypes":["chatMessage"],"query":{"queryString":"release"}}]}""", true)]
    [InlineData("teams.search_messages", """{"requests":[{"entityTypes":["chatMessage","driveItem"],"query":{"queryString":"release"}}]}""", false)]
    [InlineData("teams.search_messages", """{"requests":[{"entityTypes":["chatMessage"],"EntityTypes":["driveItem"]}]}""", false)]
    [InlineData("teams.search_messages", """{"requests":[{"entityTypes":["chatMessage"]},{"entityTypes":["message"]}]}""", false)]
    public async Task TeamsSearchOnlyAcceptsOneTeamsMessageQuery(string id, string json, bool valid)
    {
        var (provider, action) = catalog.GetAction(id);
        var input = new JsonObject { ["body"] = JsonNode.Parse(json) };
        if (!valid)
        {
            Assert.Equal("invalid_input", Assert.Throws<ConnectorException>(() => ProviderRequestBuilder.Build(provider, action, input)).Code);
            return;
        }
        using var request = ProviderRequestBuilder.Build(provider, action, input);
        Assert.True(action.IsReadOnly);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://graph.microsoft.com/v1.0/search/query", request.RequestUri!.AbsoluteUri);
        Assert.Equal(json, await request.Content!.ReadAsStringAsync());
    }

    [Fact]
    public void DataverseRecordWritesUseConfiguredEnvironmentAndDoNotUpsert()
    {
        var options = new ConnectorOptions();
        options.ProviderBaseUris["dynamics365"] = new("https://example.crm.dynamics.com/");
        var configured = new ProviderCatalog(options);
        var (provider, update) = configured.GetAction("dynamics365.update_record");
        var (_, create) = configured.GetAction("dynamics365.create_record");
        const string recordId = "00000000-0000-0000-0000-000000000001";
        using var updateRequest = ProviderRequestBuilder.Build(provider, update, new()
        {
            ["entity_set"] = "accounts", ["record_id"] = recordId, ["body"] = new JsonObject { ["name"] = "Example" }
        });
        Assert.Equal(HttpMethod.Patch, updateRequest.Method);
        Assert.Equal($"https://example.crm.dynamics.com/api/data/v9.2/accounts({recordId})", updateRequest.RequestUri!.AbsoluteUri);
        Assert.Equal("*", Assert.Single(updateRequest.Headers.IfMatch).Tag);
        Assert.Equal("4.0", Assert.Single(updateRequest.Headers.GetValues("OData-Version")));
        using var createRequest = ProviderRequestBuilder.Build(provider, create, new() { ["entity_set"] = "accounts", ["body"] = new JsonObject() });
        Assert.Equal("return=representation", Assert.Single(createRequest.Headers.GetValues("Prefer")));
        Assert.Equal("uuid", ActionSchemas.Input(update)["properties"]!["record_id"]!["format"]!.GetValue<string>());
        Assert.Throws<ConnectorException>(() => ProviderRequestBuilder.Build(provider, create, new() { ["entity_set"] = "GrantAccess", ["body"] = new JsonObject() }));
        Assert.Throws<ConnectorException>(() => ProviderRequestBuilder.Build(provider, update, new()
        {
            ["entity_set"] = "accounts", ["record_id"] = "name='Example'", ["body"] = new JsonObject()
        }));
    }
}
