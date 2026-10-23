using AgentConnector;

namespace AgentConnector.Tests;

public sealed class PolicyTests
{
    [Fact]
    public void DefaultsDenyActionsAndProxies()
    {
        var policy = new ConnectorPolicy(new ConnectorOptions());
        Assert.Equal("action_not_allowed", Assert.Throws<ConnectorException>(() => policy.DemandAction("github.get_current_user")).Code);
        Assert.Equal("proxy_not_allowed", Assert.Throws<ConnectorException>(() => policy.DemandProxy("github")).Code);
    }

    [Fact]
    public void BlockRulesWinAndProviderRulesHaveAnExactBoundary()
    {
        var options = new ConnectorOptions();
        options.AllowedActions.Add("github.*");
        options.BlockedActions.Add("github.delete_repository");
        var policy = new ConnectorPolicy(options);
        policy.DemandAction("github.get_current_user");
        Assert.Throws<ConnectorException>(() => policy.DemandAction("github.delete_repository"));
        Assert.Throws<ConnectorException>(() => policy.DemandAction("githubenterprise.get_current_user"));
        Assert.Throws<ConnectorException>(() => policy.DemandProxy("github"));
    }
}