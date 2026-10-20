using System.Net;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AgentConnector;

public enum CredentialKind
{
    Bearer,
    ApiKey,
    Basic,
    [JsonStringEnumMemberName("oauth2")]
    OAuth2,
    None
}
public enum ParameterLocation { Path, Query }
public enum ParameterKind { String, Integer, Boolean }

public sealed record ActionParameter(
    string Name,
    ParameterLocation Location = ParameterLocation.Query,
    bool Required = false,
    ParameterKind Kind = ParameterKind.String,
    string? WireName = null);

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

public sealed record OAuthEndpoints(
    Uri Authorization,
    Uri Token,
    bool UsePkce = true,
    bool UseBasicClientAuthentication = false,
    string ScopeSeparator = " ",
    IReadOnlyDictionary<string, string>? AuthorizationParameters = null,
    bool UseJsonTokenBody = false);

public sealed record ConnectorProvider(
    string Id,
    string DisplayName,
    Uri BaseUri,
    CredentialKind CredentialKind,
    IReadOnlyList<ConnectorAction> Actions,
    string? ApiKeyHeader = null,
    string? ApiKeyQueryParameter = null,
    IReadOnlyDictionary<string, string>? Headers = null,
    OAuthEndpoints? OAuth = null,
    bool RequiresBaseUri = false);

public sealed record ConnectionKey(string TenantId, string ProviderId, string Name = "default");

public sealed record ConnectorCredential(
    CredentialKind Kind,
    string Secret,
    string? UserName = null,
    string? RefreshToken = null,
    DateTimeOffset? ExpiresAt = null,
    IReadOnlyList<string>? Scopes = null)
{
    public override string ToString() => $"{nameof(ConnectorCredential)} {{ Kind = {Kind}, Secret = [REDACTED] }}";
}

public sealed record ConnectionInfo(string ProviderId, string Name, CredentialKind Kind, DateTimeOffset? ExpiresAt);
public sealed record ActionResult(JsonNode? Data, int Status, string ExecutionId);
public sealed record ConnectorHttpResponse(JsonNode? Data, int Status);
public sealed record ProxyRequest(string Endpoint, string Method = "GET", JsonObject? Query = null, JsonNode? Body = null);
public sealed record ExecutionAudit(string ExecutionId, string TenantId, string ProviderId, string ActionId,
    string ConnectionName, DateTimeOffset StartedAt, TimeSpan Duration, bool Success, string? ErrorCode);

public sealed class ConnectorException(string code, string message, HttpStatusCode status = HttpStatusCode.BadRequest)
    : Exception(message)
{
    public string Code { get; } = code;
    public HttpStatusCode Status { get; } = status;
    public TimeSpan? RetryAfter { get; init; }
}

public interface ICredentialStore
{
    ValueTask<ConnectorCredential?> GetAsync(ConnectionKey key, CancellationToken cancellationToken = default);
    ValueTask SetAsync(ConnectionKey key, ConnectorCredential credential, CancellationToken cancellationToken = default);
    ValueTask DeleteAsync(ConnectionKey key, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ConnectionInfo> ListAsync(string tenantId, CancellationToken cancellationToken = default);
}

public interface IConnectorAuditSink
{
    ValueTask WriteAsync(ExecutionAudit audit, CancellationToken cancellationToken = default);
}