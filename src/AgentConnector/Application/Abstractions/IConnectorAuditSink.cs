namespace AgentConnector;

public interface IConnectorAuditSink
{
    ValueTask WriteAsync(ExecutionAudit audit, CancellationToken cancellationToken = default);
}