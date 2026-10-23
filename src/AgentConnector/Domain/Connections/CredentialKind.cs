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