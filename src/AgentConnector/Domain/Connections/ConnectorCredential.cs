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
            Secret.Length > 32768 || Secret.Any(char.IsControl) || UserName?.Length > 32768 || UserName?.Any(char.IsControl) == true ||
            (RefreshToken is not null && (RefreshToken.Length is 0 or > 32768 || RefreshToken.Any(char.IsControl))) ||
            (Scopes is not null && (Scopes.Count > 256 || Scopes.Any(scope => string.IsNullOrWhiteSpace(scope) ||
                scope.Length > 2048 || scope.Any(character => char.IsControl(character) || char.IsWhiteSpace(character))))) ||
            (Kind is CredentialKind.Bearer or CredentialKind.OAuth2 && Secret.Any(char.IsWhiteSpace)) ||
            (Kind == CredentialKind.Basic && (string.IsNullOrWhiteSpace(UserName) || UserName.Contains(':'))) ||
            (Kind == CredentialKind.ApiKey && provider.ApiKeyHeader is null && provider.ApiKeyQueryParameter is null))
            throw new ConnectorException("invalid_input", "Invalid or unsupported provider credential.");
    }

    public override string ToString() => $"{nameof(ConnectorCredential)} {{ Kind = {Kind}, Secret = [REDACTED] }}";
}
