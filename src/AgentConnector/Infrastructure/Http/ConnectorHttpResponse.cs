using System.Text.Json.Nodes;

namespace AgentConnector;

public sealed record ConnectorHttpResponse(JsonNode? Data, int Status);