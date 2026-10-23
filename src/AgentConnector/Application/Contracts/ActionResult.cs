using System.Text.Json.Nodes;

namespace AgentConnector;

public sealed record ActionResult(JsonNode? Data, int Status, string ExecutionId);