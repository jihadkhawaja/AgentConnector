using System.Net;

namespace AgentConnector.StorageSample;

public sealed record VaultStoreOptions(Uri Address, string Mount, string Token, bool AllowDevelopmentLoopbackHttp = false)
{
    public void Validate()
    {
        if (!Address.IsAbsoluteUri || Address.AbsolutePath != "/" || Address.Query.Length != 0 ||
            Address.Fragment.Length != 0 || Address.UserInfo.Length != 0 ||
            (Address.Scheme != Uri.UriSchemeHttps && !(AllowDevelopmentLoopbackHttp && Address.Scheme == Uri.UriSchemeHttp &&
                IPAddress.TryParse(Address.Host, out var address) && IPAddress.IsLoopback(address))))
            throw new ArgumentException("Vault requires an HTTPS origin, or an explicitly enabled numeric loopback HTTP origin for development.");
        if (Mount.Length is < 1 or > 64 || Mount.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            throw new ArgumentException("Vault mount must be a single alphanumeric, hyphen, or underscore segment.");
        if (string.IsNullOrWhiteSpace(Token) || Token.Length > 4096 || Token.Any(character => char.IsControl(character) || char.IsWhiteSpace(character)))
            throw new ArgumentException("Configure a valid Vault token through a secret manager or environment variable.");
    }

    public override string ToString() => $"{nameof(VaultStoreOptions)} {{ Token = [REDACTED] }}";
}
