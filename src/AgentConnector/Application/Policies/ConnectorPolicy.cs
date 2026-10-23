namespace AgentConnector;

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