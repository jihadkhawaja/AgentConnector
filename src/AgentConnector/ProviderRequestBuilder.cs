using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.WebUtilities;

namespace AgentConnector;

public static class ProviderRequestBuilder
{
    public static HttpRequestMessage Build(ConnectorProvider provider, ConnectorAction action, JsonObject input)
    {
        var known = action.Parameters.Select(parameter => parameter.Name).ToHashSet(StringComparer.Ordinal);
        if (action.AcceptsBody) known.Add("body");
        if (input.Any(property => !known.Contains(property.Key))) throw Invalid("Unknown action input field.");
        var path = action.Path;
        var query = new Dictionary<string, string?>();
        foreach (var parameter in action.Parameters)
        {
            var value = input[parameter.Name];
            if (value is null)
            {
                if (parameter.Required) throw Invalid($"Required input '{parameter.Name}' is missing.");
                continue;
            }
            if (value is not JsonValue scalar) throw Invalid($"Input '{parameter.Name}' must be a scalar.");
            var text = parameter.Kind switch
            {
                ParameterKind.String when scalar.TryGetValue<string>(out var result) => result,
                ParameterKind.Integer when scalar.TryGetValue<long>(out var result) => result.ToString(CultureInfo.InvariantCulture),
                ParameterKind.Integer when scalar.TryGetValue<int>(out var result) => result.ToString(CultureInfo.InvariantCulture),
                ParameterKind.Boolean when scalar.TryGetValue<bool>(out var result) => result ? "true" : "false",
                _ => throw Invalid($"Input '{parameter.Name}' has the wrong type.")
            };
            if (parameter.Location == ParameterLocation.Path)
            {
                if (string.IsNullOrWhiteSpace(text) || text is "." or ".." || text.IndexOfAny(['/', '\\', '%']) >= 0 || text.Any(char.IsControl))
                    throw Invalid($"Path input '{parameter.Name}' must be a single safe segment.");
                path = path.Replace("{" + parameter.Name + "}", Uri.EscapeDataString(text), StringComparison.Ordinal);
            }
            else query[parameter.WireName ?? parameter.Name] = text;
        }
        var request = new HttpRequestMessage(action.Method, QueryHelpers.AddQueryString(BuildUri(provider, path).AbsoluteUri, query));
        try
        {
            if (action.AcceptsBody && input["body"] is { } body)
            {
                if (body is not JsonObject) throw Invalid("The body input must be a JSON object.");
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            }
            else if (action.RequiresBody) throw Invalid("Required input 'body' is missing.");
            AddProviderHeaders(request, provider);
            return request;
        }
        catch { request.Dispose(); throw; }
    }

    public static HttpRequestMessage BuildProxy(ConnectorProvider provider, ProxyRequest input)
    {
        if (input.Method is not ("GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD"))
            throw Invalid("Unsupported proxy HTTP method.");
        if (!input.Endpoint.StartsWith('/') || input.Endpoint.StartsWith("//", StringComparison.Ordinal) || input.Endpoint.Contains('%'))
            throw Invalid("Proxy endpoint must start with a single '/'.");
        var query = new Dictionary<string, string?>();
        foreach (var property in input.Query ?? [])
        {
            if (property.Value is not null && property.Value is not JsonValue)
                throw Invalid("Proxy query values must be scalars.");
            query[property.Key] = property.Value is JsonValue scalar && scalar.TryGetValue<string>(out var text)
                ? text : property.Value?.ToJsonString();
        }
        if (input.Body is not null && input.Method is "GET" or "HEAD") throw Invalid("GET and HEAD cannot contain a body.");
        var request = new HttpRequestMessage(new HttpMethod(input.Method), QueryHelpers.AddQueryString(BuildUri(provider, input.Endpoint[1..]).AbsoluteUri, query));
        if (input.Body is not null) request.Content = new StringContent(input.Body.ToJsonString(), Encoding.UTF8, "application/json");
        AddProviderHeaders(request, provider);
        return request;
    }

    public static void Authenticate(HttpRequestMessage request, ConnectorProvider provider, ConnectorCredential? credential)
    {
        if (provider.CredentialKind == CredentialKind.None) return;
        if (credential is null) throw new ConnectorException("connection_not_found", "The selected connection does not exist.", System.Net.HttpStatusCode.NotFound);
        credential.Validate(provider);
        switch (credential.Kind)
        {
            case CredentialKind.Basic:
                request.Headers.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credential.UserName + ":" + credential.Secret)));
                break;
            case CredentialKind.ApiKey:
                if (provider.ApiKeyHeader is not null) request.Headers.Add(provider.ApiKeyHeader, credential.Secret);
                else request.RequestUri = new(QueryHelpers.AddQueryString(request.RequestUri!.AbsoluteUri, provider.ApiKeyQueryParameter!, credential.Secret));
                break;
            default:
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.Secret);
                break;
        }
    }

    public static void ValidateCredential(ConnectorProvider provider, ConnectorCredential credential) => credential.Validate(provider);

    private static Uri BuildUri(ConnectorProvider provider, string path)
    {
        if (string.IsNullOrEmpty(path) || path.StartsWith('/') || path.Contains("://", StringComparison.Ordinal) || path.Contains('\\') || path.Contains('?') || path.Contains('#') ||
            path.Any(char.IsControl) || path.Split('/').Any(segment =>
                Uri.UnescapeDataString(segment) is "." or ".." || Uri.UnescapeDataString(segment).IndexOfAny(['/', '\\']) >= 0))
            throw Invalid("Unsafe provider path.");
        var uri = new Uri(provider.BaseUri, "./" + path);
        if (uri.GetLeftPart(UriPartial.Authority) != provider.BaseUri.GetLeftPart(UriPartial.Authority) ||
            !uri.AbsolutePath.StartsWith(provider.BaseUri.AbsolutePath, StringComparison.Ordinal)) throw Invalid("Provider path escaped its configured base URI.");
        NetworkGuard.ValidateHttpsUri(uri);
        return uri;
    }

    private static void AddProviderHeaders(HttpRequestMessage request, ConnectorProvider provider)
    {
        request.Headers.UserAgent.ParseAdd("AgentConnector/0.1");
        request.Headers.Accept.Add(new("application/json"));
        foreach (var header in provider.Headers ?? new Dictionary<string, string>())
        {
            request.Headers.Remove(header.Key);
            request.Headers.Add(header.Key, header.Value);
        }
    }

    private static ConnectorException Invalid(string message) => new("invalid_input", message);
}