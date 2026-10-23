namespace AgentConnector;

public sealed record ExecutionAudit(string ExecutionId, string TenantId, string ProviderId, string ActionId,
    string ConnectionName, DateTimeOffset StartedAt, TimeSpan Duration, bool Success, string? ErrorCode);