using System.Text.Json.Nodes;

namespace AgentConnector;

public sealed record ProxyRequest(string Endpoint, string Method = "GET", JsonObject? Query = null, JsonNode? Body = null);