namespace AgentConnector;

public sealed record OAuthAuthorization(Uri AuthorizationUri, DateTimeOffset ExpiresAt);