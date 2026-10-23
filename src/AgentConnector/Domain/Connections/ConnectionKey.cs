namespace AgentConnector;

public sealed record ConnectionKey(string TenantId, string ProviderId, string Name = "default")
{
    public void Validate()
    {
        ValidateIdentity(TenantId);
        ValidateIdentity(ProviderId);
        ValidateIdentity(Name);
    }

    internal static void ValidateIdentity(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl))
            throw new ConnectorException("invalid_input", "Connection identifiers must contain 1 to 256 non-control characters.");
    }
}