using System.Text.Json.Nodes;
using AgentConnector;

namespace AgentConnector.Tests;

public sealed class RequestBuilderTests
{
    private readonly ProviderCatalog catalog = new(new());

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