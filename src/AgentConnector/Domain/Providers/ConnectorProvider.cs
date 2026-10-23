namespace AgentConnector;

public sealed record ConnectorProvider(
    string Id,
    string DisplayName,
    Uri BaseUri,
    CredentialKind CredentialKind,
    IReadOnlyList<ConnectorAction> Actions,
    string? ApiKeyHeader = null,
    string? ApiKeyQueryParameter = null,
    IReadOnlyDictionary<string, string>? Headers = null,
    OAuthEndpoints? OAuth = null,
    bool RequiresBaseUri = false);