using System.Text.Json.Nodes;
using AgentConnector;

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
}
