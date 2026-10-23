namespace AgentConnector;

public sealed record ConnectionInfo(string ProviderId, string Name, CredentialKind Kind, DateTimeOffset? ExpiresAt);