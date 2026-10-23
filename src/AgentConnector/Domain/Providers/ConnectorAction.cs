namespace AgentConnector;

public sealed record ConnectorAction(
    string Id,
    string Description,
    HttpMethod Method,
    string Path,
    IReadOnlyList<ActionParameter> Parameters,
    bool AcceptsBody = false,
    bool RequiresBody = false,
    IReadOnlyList<string>? RequiredScopes = null)
{
    public bool IsReadOnly => Method == HttpMethod.Get || Method == HttpMethod.Head;
}