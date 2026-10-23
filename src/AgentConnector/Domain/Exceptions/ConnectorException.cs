using System.Net;

namespace AgentConnector;

public sealed class ConnectorException(string code, string message, HttpStatusCode status = HttpStatusCode.BadRequest)
    : Exception(message)
{
    public string Code { get; } = code;
    public HttpStatusCode Status { get; } = status;
    public TimeSpan? RetryAfter { get; init; }
}