using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentConnector;

public sealed class ConnectorTransport(HttpClient client, ConnectorOptions options)
{
    public async Task<ConnectorHttpResponse> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        NetworkGuard.ValidateHttpsUri(request.RequestUri!);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(options.RequestTimeout);
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token);
            if (!response.IsSuccessStatusCode)
            {
                var code = response.StatusCode switch
                {
                    HttpStatusCode.TooManyRequests => "rate_limited",
                    HttpStatusCode.Unauthorized => "credential_rejected",
                    HttpStatusCode.Forbidden => "provider_forbidden",
                    _ => "provider_error"
                };
                var retry = response.Headers.RetryAfter?.Delta;
                if (retry is null && response.Headers.RetryAfter?.Date is { } date)
                    retry = date - DateTimeOffset.UtcNow;
                throw new ConnectorException(code, $"Provider returned HTTP {(int)response.StatusCode}.",
                    response.StatusCode == HttpStatusCode.TooManyRequests ? HttpStatusCode.TooManyRequests : HttpStatusCode.BadGateway)
                { RetryAfter = retry > TimeSpan.Zero ? retry : null };
            }
            if (response.Content.Headers.ContentLength > options.MaximumResponseBytes)
                throw TooLarge();
            await using var stream = await response.Content.ReadAsStreamAsync(budget.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(chunk, budget.Token)) != 0)
            {
                if (buffer.Length + count > options.MaximumResponseBytes) throw TooLarge();
                buffer.Write(chunk, 0, count);
            }
            if (buffer.Length == 0) return new(null, (int)response.StatusCode);
            try { return new(JsonNode.Parse(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)), (int)response.StatusCode); }
            catch (JsonException)
            {
                throw new ConnectorException("invalid_provider_response", "Provider returned a non-JSON response.", HttpStatusCode.BadGateway);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ConnectorException("provider_timeout", "Provider request timed out; the operation may have completed.", HttpStatusCode.GatewayTimeout);
        }
        catch (HttpRequestException)
        {
            throw new ConnectorException("provider_unreachable", "Provider could not be reached; the operation may have completed.", HttpStatusCode.BadGateway);
        }
    }

    private static ConnectorException TooLarge() => new("response_too_large", "Provider response exceeded the configured limit.", HttpStatusCode.BadGateway);
}