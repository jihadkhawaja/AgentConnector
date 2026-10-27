using Microsoft.Extensions.Logging;

namespace AgentConnector;

public sealed class LoggingConnectorAuditSink(ILogger<LoggingConnectorAuditSink> logger) : IConnectorAuditSink
{
    public ValueTask WriteAsync(ExecutionAudit audit, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Connector execution {ExecutionId} action {ActionId} success {Success} error {ErrorCode} duration {DurationMs}ms",
            ForLog(audit.ExecutionId), ForLog(audit.ActionId), audit.Success, ForLog(audit.ErrorCode), audit.Duration.TotalMilliseconds);
        return ValueTask.CompletedTask;
    }

    // Sanitize before ILogger receives the values so structured state is safe too.
    private static string? ForLog(string? value) => value?.ReplaceLineEndings("\\n");
}
