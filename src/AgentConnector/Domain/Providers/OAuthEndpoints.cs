namespace AgentConnector;

public sealed record OAuthEndpoints(
    Uri Authorization,
    Uri Token,
    bool UsePkce = true,
    bool UseBasicClientAuthentication = false,
    string ScopeSeparator = " ",
    IReadOnlyDictionary<string, string>? AuthorizationParameters = null,
    bool UseJsonTokenBody = false);