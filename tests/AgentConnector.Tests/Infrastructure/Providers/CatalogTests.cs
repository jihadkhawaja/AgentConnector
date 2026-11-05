using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AgentConnector;

namespace AgentConnector.Tests;

public sealed class CatalogTests
{
    [Fact]
    public void BuiltInActionsHaveUniqueIdsSafeHostsAndCompletePathParameters()
    {
        var catalog = new ProviderCatalog(new());
        Assert.True(catalog.Providers.Count >= 45);
        Assert.True(catalog.Actions.Count >= 140);
        Assert.Equal(catalog.Actions.Count, catalog.Actions.Select(action => action.Id).Distinct().Count());
        foreach (var provider in catalog.Providers)
        {
            NetworkGuard.ValidateHttpsUri(provider.BaseUri);
            foreach (var action in provider.Actions)
            {
                Assert.StartsWith(provider.Id + ".", action.Id);
                foreach (Match match in Regex.Matches(action.Path, "\\{([^}]+)\\}"))
                    Assert.Contains(action.Parameters, parameter => parameter.Name == match.Groups[1].Value && parameter.Location == ParameterLocation.Path && parameter.Required);
            }
        }
    }

    [Theory]
    [InlineData("github")]
    [InlineData("gmail")]
    [InlineData("outlook")]
    [InlineData("slack")]
    [InlineData("notion")]
    [InlineData("salesforce")]
    [InlineData("stripe")]
    [InlineData("openai")]
    [InlineData("anthropic")]
    public void MajorProvidersHaveExecutableActionDefinitions(string id) =>
        Assert.NotEmpty(new ProviderCatalog(new()).GetProvider(id).Actions);

    [Fact]
    public void EveryCatalogActionBuildsWithItsDeclaredAuthenticationAndSchema()
    {
        var catalog = new ProviderCatalog(new());
        Assert.Equal(49, catalog.Providers.Count);
        Assert.Equal(221, catalog.Actions.Count);
        foreach (var provider in catalog.Providers)
        {
            foreach (var action in provider.Actions)
            {
                var schema = ActionSchemas.Input(action);
                var input = new JsonObject();
                foreach (var parameter in action.Parameters.Where(parameter => parameter.Required))
                    input[parameter.Name] = parameter.Kind switch
                    {
                        ParameterKind.Integer => JsonValue.Create(1),
                        ParameterKind.Boolean => JsonValue.Create(true),
                        ParameterKind.Guid => JsonValue.Create("00000000-0000-0000-0000-000000000001"),
                        _ => schema["properties"]![parameter.Name]!["enum"]?[0]?.DeepClone() ?? JsonValue.Create("sample-id")
                    };
                if (action.RequiresBody)
                    input["body"] = action.Id == "teams.search_messages"
                        ? JsonNode.Parse("""{"requests":[{"entityTypes":["chatMessage"],"query":{"queryString":"sample"}}]}""")
                        : new JsonObject();
                using var request = ProviderRequestBuilder.Build(provider, action, input);
                var credential = provider.CredentialKind == CredentialKind.None ? null :
                    new ConnectorCredential(provider.CredentialKind, "sample-secret", UserName: "sample-user");
                ProviderRequestBuilder.Authenticate(request, provider, credential);
                Assert.Equal(provider.BaseUri.Host, request.RequestUri!.Host);
                Assert.Equal(action.Method, request.Method);
            }
        }
    }
}
