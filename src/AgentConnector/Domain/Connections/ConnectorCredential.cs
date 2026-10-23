namespace AgentConnector;

public sealed record ConnectorCredential(
    CredentialKind Kind,
    string Secret,
    string? UserName = null,
    string? RefreshToken = null,
    DateTimeOffset? ExpiresAt = null,
    IReadOnlyList<string>? Scopes = null)
{
    public void Validate(ConnectorProvider provider)
    {
        if (!Enum.IsDefined(Kind) || Kind == CredentialKind.None || string.IsNullOrWhiteSpace(Secret) ||
            Secret.Length > 32768 || Secret.Any(char.IsControl) || UserName?.Any(char.IsControl) == true ||
            (Kind is CredentialKind.Bearer or CredentialKind.OAuth2 && Secret.Any(char.IsWhiteSpace)) ||
            (Kind == CredentialKind.Basic && (string.IsNullOrWhiteSpace(UserName) || UserName.Contains(':'))) ||
            (Kind == CredentialKind.ApiKey && provider.ApiKeyHeader is null && provider.ApiKeyQueryParameter is null))
            throw new ConnectorException("invalid_input", "Invalid or unsupported provider credential.");
    }

    public override string ToString() => $"{nameof(ConnectorCredential)} {{ Kind = {Kind}, Secret = [REDACTED] }}";
}