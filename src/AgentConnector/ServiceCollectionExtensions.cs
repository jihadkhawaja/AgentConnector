using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentConnector;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAgentConnector(this IServiceCollection services, Action<ConnectorOptions>? configure = null)
    {
        var options = new ConnectorOptions();
        configure?.Invoke(options);
        if (options.MaximumRequestBytes is <= 0 or > 64 * 1024 * 1024 || options.MaximumResponseBytes is <= 0 or > 64 * 1024 * 1024 ||
            options.RequestTimeout <= TimeSpan.Zero || options.RequestTimeout > TimeSpan.FromMinutes(10) ||
            string.IsNullOrWhiteSpace(options.CredentialDirectory) || string.IsNullOrWhiteSpace(options.TenantClaimType))
            throw new ArgumentException("Connector limits and storage settings are invalid.");
        services.AddSingleton(options);
        services.AddLogging();
        services.AddDataProtection();
        services.AddAuthorization(authorization =>
        {
            if (authorization.GetPolicy(options.RuntimePolicyName) is null)
                authorization.AddPolicy(options.RuntimePolicyName, policy => policy.RequireAuthenticatedUser());
            if (authorization.GetPolicy(options.AdminPolicyName) is null)
                authorization.AddPolicy(options.AdminPolicyName, policy => policy.RequireAuthenticatedUser().RequireClaim("connector_admin", "true"));
        });
        services.TryAddSingleton<ICredentialStore, EncryptedFileCredentialStore>();
        services.TryAddSingleton<IConnectorAuditSink, LoggingConnectorAuditSink>();
        services.AddSingleton<ProviderCatalog>();
        services.AddSingleton<ConnectorPolicy>();
        services.AddSingleton<ConnectionCoordinator>();
        services.AddHttpClient<ConnectorTransport>(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(NetworkGuard.CreateHandler)
            .RemoveAllLoggers();
        services.AddSingleton<ConnectorOAuth>();
        services.AddSingleton<ConnectorRuntime>();
        return services;
    }
}