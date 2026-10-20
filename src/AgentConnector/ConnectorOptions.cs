namespace AgentConnector;

public sealed class ConnectorOptions
{
    public string CredentialDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, ".data", "credentials");
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int MaximumResponseBytes { get; set; } = 4 * 1024 * 1024;
    public int MaximumRequestBytes { get; set; } = 1024 * 1024;
    public string TenantClaimType { get; set; } = "tenant_id";
    public string RuntimePolicyName { get; set; } = "AgentConnector.Runtime";
    public string AdminPolicyName { get; set; } = "AgentConnector.Admin";
    public HashSet<string> AllowedActions { get; } = new(StringComparer.Ordinal);
    public HashSet<string> BlockedActions { get; } = new(StringComparer.Ordinal);
    public HashSet<string> AllowedProxies { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, Uri> ProviderBaseUris { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, OAuthClientOptions> OAuthClients { get; } = new(StringComparer.Ordinal);
}

public sealed class OAuthClientOptions
{
    public required string ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public required Uri RedirectUri { get; init; }
    public IReadOnlyList<string> Scopes { get; init; } = [];
    public OAuthEndpoints? Endpoints { get; init; }
    public override string ToString() => $"{nameof(OAuthClientOptions)} {{ ClientId = {ClientId}, ClientSecret = [REDACTED] }}";
}

public sealed class ConnectorPolicy(ConnectorOptions options)
{
    public bool IsActionAllowed(string actionId) =>
        !options.BlockedActions.Any(rule => Matches(rule, actionId)) && options.AllowedActions.Any(rule => Matches(rule, actionId));

    public void DemandAction(string actionId)
    {
        if (!IsActionAllowed(actionId))
            throw new ConnectorException("action_not_allowed", "This action is not allowed.", System.Net.HttpStatusCode.Forbidden);
    }

    public void DemandProxy(string providerId)
    {
        if (!options.AllowedProxies.Contains(providerId) && !options.AllowedProxies.Contains("*"))
            throw new ConnectorException("proxy_not_allowed", "Provider proxy access is not allowed.", System.Net.HttpStatusCode.Forbidden);
    }

    private static bool Matches(string rule, string actionId) => rule == "*" || rule == actionId ||
        (rule.EndsWith(".*", StringComparison.Ordinal) &&
         actionId.StartsWith(rule[..^1], StringComparison.Ordinal));
}