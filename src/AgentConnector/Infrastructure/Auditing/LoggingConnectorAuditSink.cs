using Microsoft.Extensions.Logging;

namespace AgentConnector;

public sealed class LoggingConnectorAuditSink(ILogger<LoggingConnectorAuditSink> logger) : IConnectorAuditSink
{
    public ValueTask WriteAsync(ExecutionAudit audit, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Connector execution {ExecutionId} action {ActionId} success {Success} error {ErrorCode} duration {DurationMs}ms",
            audit.ExecutionId, audit.ActionId, audit.Success, audit.ErrorCode, audit.Duration.TotalMilliseconds);
        return ValueTask.CompletedTask;
    }
}