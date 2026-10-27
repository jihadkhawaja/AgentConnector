using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.WebUtilities;

namespace AgentConnector;

public sealed class ConnectorOAuth(ConnectorOptions options, ProviderCatalog catalog, ICredentialStore credentials,
    ConnectionCoordinator coordinator, ConnectorTransport transport)
{
    private sealed record PendingAuthorization(ConnectionKey Key, string Verifier, long Version, DateTimeOffset ExpiresAt);
    private readonly ConcurrentDictionary<string, PendingAuthorization> pending = new(StringComparer.Ordinal);
    private readonly object pendingGate = new();

    public async Task<OAuthAuthorization> BeginAsync(ConnectionKey key, CancellationToken cancellationToken = default)
    {
        key.Validate();
        var provider = catalog.GetProvider(key.ProviderId);
        var (client, endpoints) = Configuration(provider);
        using var lease = await coordinator.LockAsync(key, cancellationToken);
        var state = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var expires = DateTimeOffset.UtcNow.AddMinutes(10);
        lock (pendingGate)
        {
            foreach (var entry in pending.Where(entry => entry.Value.ExpiresAt <= DateTimeOffset.UtcNow || entry.Value.Key == key))
                pending.TryRemove(entry.Key, out _);
            if (pending.Count >= 1024)
                throw new ConnectorException("oauth_capacity", "Too many pending authorization requests.", HttpStatusCode.TooManyRequests);
            pending[state] = new(key, verifier, coordinator.Invalidate(key), expires);
        }
        var query = endpoints.AuthorizationParameters?.ToDictionary(entry => entry.Key, entry => (string?)entry.Value) ?? new();
        query["client_id"] = client.ClientId;
        query["redirect_uri"] = client.RedirectUri.AbsoluteUri;
        query["response_type"] = "code";
        query["state"] = state;
        if (client.Scopes.Count != 0) query["scope"] = string.Join(endpoints.ScopeSeparator, client.Scopes);
        if (endpoints.UsePkce)
        {
            query["code_challenge"] = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            query["code_challenge_method"] = "S256";
        }
        return new(new(QueryHelpers.AddQueryString(endpoints.Authorization.AbsoluteUri, query)), expires);
    }

    public async Task<ConnectionInfo> CompleteAsync(string state, string? code, string? error = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(state) || state.Length > 128 || !pending.TryRemove(state, out var authorization) ||
            authorization.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new ConnectorException("invalid_oauth_state", "Authorization state is invalid, expired, or already used.");
        if (error is not null || string.IsNullOrWhiteSpace(code) || code.Length > 8192)
            throw new ConnectorException("oauth_denied", "The provider did not authorize this connection.");
        using var lease = await coordinator.LockAsync(authorization.Key, cancellationToken);
        if (coordinator.Version(authorization.Key) != authorization.Version)
            throw new ConnectorException("stale_oauth_state", "The connection changed after authorization began.", HttpStatusCode.Conflict);
        var provider = catalog.GetProvider(authorization.Key.ProviderId);
        var (client, endpoints) = Configuration(provider);
        var fields = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = client.RedirectUri.AbsoluteUri
        };
        if (endpoints.UsePkce) fields["code_verifier"] = authorization.Verifier;
        var credential = await ExchangeAsync(provider, client, endpoints, fields, null, cancellationToken);
        await credentials.SetAsync(authorization.Key, credential, cancellationToken);
        coordinator.Invalidate(authorization.Key);
        return new(authorization.Key.ProviderId, authorization.Key.Name, credential.Kind, credential.ExpiresAt);
    }

    internal async Task<ConnectorCredential> RefreshAsync(ConnectorProvider provider, ConnectorCredential credential,
        CancellationToken cancellationToken = default)
    {
        if (credential.Kind != CredentialKind.OAuth2 || string.IsNullOrEmpty(credential.RefreshToken))
            throw new ConnectorException("credential_expired", "This credential has expired and must be reconnected.", HttpStatusCode.Unauthorized);
        var (client, endpoints) = Configuration(provider);
        return await ExchangeAsync(provider, client, endpoints, new()
        {
            ["grant_type"] = "refresh_token", ["refresh_token"] = credential.RefreshToken
        }, credential, cancellationToken);
    }

    private (OAuthClientOptions Client, OAuthEndpoints Endpoints) Configuration(ConnectorProvider provider)
    {
        if (!options.OAuthClients.TryGetValue(provider.Id, out var client) || (client.Endpoints ?? provider.OAuth) is not { } endpoints)
            throw new ConnectorException("oauth_not_configured", "OAuth is not configured for this provider.");
        NetworkGuard.ValidateHttpsUri(endpoints.Authorization);
        NetworkGuard.ValidateHttpsUri(endpoints.Token);
        if (string.IsNullOrWhiteSpace(client.ClientId) || client.ClientId.Any(char.IsControl) ||
            !client.RedirectUri.IsAbsoluteUri || client.RedirectUri.UserInfo.Length != 0 || client.RedirectUri.Fragment.Length != 0 ||
            (client.RedirectUri.Scheme != "https" && !(client.RedirectUri.Scheme == "http" && client.RedirectUri.IsLoopback)))
            throw new ConnectorException("oauth_not_configured", "OAuth client or redirect URI is invalid.");
        if (endpoints.UseBasicClientAuthentication && string.IsNullOrEmpty(client.ClientSecret))
            throw new ConnectorException("oauth_not_configured", "This OAuth provider requires a client secret.");
        return (client, endpoints);
    }

    private async Task<ConnectorCredential> ExchangeAsync(ConnectorProvider provider, OAuthClientOptions client, OAuthEndpoints endpoints,
        Dictionary<string, string> fields, ConnectorCredential? previous, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoints.Token);
        request.Headers.Accept.Add(new("application/json"));
        if (endpoints.UseBasicClientAuthentication)
        {
            var id = endpoints.UseJsonTokenBody ? client.ClientId : WebUtility.UrlEncode(client.ClientId);
            var secret = endpoints.UseJsonTokenBody ? client.ClientSecret : WebUtility.UrlEncode(client.ClientSecret);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(id + ":" + secret)));
        }
        else
        {
            fields["client_id"] = client.ClientId;
            if (!string.IsNullOrEmpty(client.ClientSecret)) fields["client_secret"] = client.ClientSecret;
        }
        request.Content = endpoints.UseJsonTokenBody
            ? new StringContent(new JsonObject(fields.Select(entry => KeyValuePair.Create<string, JsonNode?>(entry.Key, JsonValue.Create(entry.Value)))).ToJsonString(), Encoding.UTF8, "application/json")
            : new FormUrlEncodedContent(fields);
        var response = (await transport.SendAsync(request, cancellationToken)).Data as JsonObject;
        if (response is null || response["error"] is not null || response["ok"]?.ToString() == "false" ||
            response["access_token"] is not JsonValue tokenValue || !tokenValue.TryGetValue<string>(out var token) || string.IsNullOrWhiteSpace(token) || token.Any(char.IsControl))
            throw new ConnectorException("oauth_exchange_failed", "Provider did not return a valid access token.", HttpStatusCode.BadGateway);
        if (response["token_type"] is { } type && !string.Equals(type.ToString(), "bearer", StringComparison.OrdinalIgnoreCase) && type.ToString() != "bot")
            throw new ConnectorException("oauth_exchange_failed", "Provider returned an unsupported token type.", HttpStatusCode.BadGateway);
        DateTimeOffset? expires = null;
        if (response["expires_in"] is { } lifetime)
        {
            if (!long.TryParse(lifetime.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) || seconds is <= 0 or > 315360000)
                throw new ConnectorException("oauth_exchange_failed", "Provider returned an invalid token lifetime.", HttpStatusCode.BadGateway);
            expires = DateTimeOffset.UtcNow.AddSeconds(seconds);
        }
        var scopes = OptionalString(response, "scope")?.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries) ?? previous?.Scopes;
        var credential = new ConnectorCredential(CredentialKind.OAuth2, token,
            RefreshToken: OptionalString(response, "refresh_token") ?? previous?.RefreshToken, ExpiresAt: expires, Scopes: scopes);
        try { credential.Validate(provider); }
        catch (ConnectorException)
        {
            throw new ConnectorException("oauth_exchange_failed", "Provider returned an invalid credential.", HttpStatusCode.BadGateway);
        }
        return credential;
    }

    private static string? OptionalString(JsonObject response, string name)
    {
        if (response[name] is null) return null;
        if (response[name] is JsonValue value && value.TryGetValue<string>(out var text)) return text;
        throw new ConnectorException("oauth_exchange_failed", "Provider returned an invalid credential field.", HttpStatusCode.BadGateway);
    }
}
