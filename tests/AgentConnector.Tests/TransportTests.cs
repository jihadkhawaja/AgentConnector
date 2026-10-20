using System.Net;
using AgentConnector;

namespace AgentConnector.Tests;

public sealed class TransportTests
{
    [Fact]
    public void ProductionHandlerDoesNotFollowRedirectsUseCookiesOrEnvironmentProxies()
    {
        using var handler = NetworkGuard.CreateHandler();
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
        Assert.False(handler.UseProxy);
        Assert.NotNull(handler.ConnectCallback);
    }

    [Theory]
    [InlineData(301, "provider_error")]
    [InlineData(401, "credential_rejected")]
    [InlineData(403, "provider_forbidden")]
    [InlineData(429, "rate_limited")]
    [InlineData(500, "provider_error")]
    public async Task RejectsErrorsWithoutReturningBodiesOrRetrying(int status, string code)
    {
        var calls = 0;
        using var client = new HttpClient(new Handler((_, _) =>
        {
            calls++;
            var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("secret-provider-error-body") };
            response.Headers.RetryAfter = new(TimeSpan.FromSeconds(7));
            response.Headers.Location = new("https://evil.example/");
            return Task.FromResult(response);
        }));
        var transport = new ConnectorTransport(client, new());
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
        var exception = await Assert.ThrowsAsync<ConnectorException>(() => transport.SendAsync(request));
        Assert.Equal(code, exception.Code);
        Assert.DoesNotContain("secret-provider-error-body", exception.ToString());
        Assert.Equal(TimeSpan.FromSeconds(7), exception.RetryAfter);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EnforcesResponseLimitWithOrWithoutContentLength(bool knownLength)
    {
        using var client = new HttpClient(new Handler((_, _) =>
        {
            HttpContent content = knownLength ? new StringContent(new string('x', 200)) : new StreamContent(new NonSeekableStream(new byte[200]));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }));
        var transport = new ConnectorTransport(client, new() { MaximumResponseBytes = 100 });
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
        Assert.Equal("response_too_large", (await Assert.ThrowsAsync<ConnectorException>(() => transport.SendAsync(request))).Code);
    }

    [Fact]
    public async Task DistinguishesTimeoutFromCallerCancellation()
    {
        using var client = new HttpClient(new Handler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new(HttpStatusCode.OK);
        }));
        var transport = new ConnectorTransport(client, new() { RequestTimeout = TimeSpan.FromMilliseconds(50) });
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
        Assert.Equal("provider_timeout", (await Assert.ThrowsAsync<ConnectorException>(() => transport.SendAsync(request))).Code);
        using var cancelledRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transport.SendAsync(cancelledRequest, cancellation.Token));
    }

    [Fact]
    public async Task PreservesNoContentProviderStatus()
    {
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent))));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://graph.microsoft.com/v1.0/me/sendMail");
        var response = await new ConnectorTransport(client, new()).SendAsync(request);
        Assert.Equal(204, response.Status);
        Assert.Null(response.Data);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}