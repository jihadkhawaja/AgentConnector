namespace AgentConnector;

public sealed record ActionParameter(
    string Name,
    ParameterLocation Location = ParameterLocation.Query,
    bool Required = false,
    ParameterKind Kind = ParameterKind.String,
    string? WireName = null);