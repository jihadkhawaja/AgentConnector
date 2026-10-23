namespace AgentConnector;

public sealed class OAuthClientOptions
{
    public required string ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public required Uri RedirectUri { get; init; }
    public IReadOnlyList<string> Scopes { get; init; } = [];
    public OAuthEndpoints? Endpoints { get; init; }
    public override string ToString() => $"{nameof(OAuthClientOptions)} {{ ClientId = {ClientId}, ClientSecret = [REDACTED] }}";
}