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