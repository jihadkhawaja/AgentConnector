using AgentConnector;
using Microsoft.Extensions.Logging;

namespace AgentConnector.Tests;

public sealed class LoggingConnectorAuditSinkTests
{
    [Theory]
    [InlineData("first\r\nforged entry", "first\\nforged entry")]
    [InlineData("first\rforged entry", "first\\nforged entry")]
    [InlineData("first\nforged entry", "first\\nforged entry")]
    [InlineData("first\fforged entry", "first\\nforged entry")]
    [InlineData("first\u0085forged entry", "first\\nforged entry")]
    [InlineData("first\u2028forged entry", "first\\nforged entry")]
    [InlineData("first\u2029forged entry", "first\\nforged entry")]
    public async Task EscapesLineBreaksInRenderedAndStructuredLogValues(string input, string expected)
    {
        var logger = new CapturingLogger();
        var sink = new LoggingConnectorAuditSink(logger);
        var audit = new ExecutionAudit(input, "unlogged-tenant", "unlogged-provider", input,
            "unlogged-connection", DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(12), false, input);

        await sink.WriteAsync(audit);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(expected, entry.Properties["ExecutionId"]);
        Assert.Equal(expected, entry.Properties["ActionId"]);
        Assert.Equal(expected, entry.Properties["ErrorCode"]);
        Assert.Equal($"Connector execution {expected} action {expected} success False error {expected} duration 12ms", entry.Message);
        Assert.DoesNotContain("unlogged-tenant", entry.Message);
        Assert.DoesNotContain("unlogged-provider", entry.Message);
        Assert.DoesNotContain("unlogged-connection", entry.Message);
    }

    [Fact]
    public async Task PreservesOrdinaryMetadataAndNullErrorCode()
    {
        var logger = new CapturingLogger();
        var sink = new LoggingConnectorAuditSink(logger);
        var audit = new ExecutionAudit("execution-123", "tenant", "github", "github.get_current_user",
            "default", DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(12), true, null);

        await sink.WriteAsync(audit);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal("execution-123", entry.Properties["ExecutionId"]);
        Assert.Equal("github.get_current_user", entry.Properties["ActionId"]);
        Assert.Null(entry.Properties["ErrorCode"]);
        Assert.True(Assert.IsType<bool>(entry.Properties["Success"]));
        Assert.Equal(12d, Assert.IsType<double>(entry.Properties["DurationMs"]));
    }

    private sealed class CapturingLogger : ILogger<LoggingConnectorAuditSink>
    {
        public List<(string Message, IReadOnlyDictionary<string, object?> Properties)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = Assert.IsAssignableFrom<IEnumerable<KeyValuePair<string, object?>>>(state);
            Entries.Add((formatter(state, exception), properties.ToDictionary(property => property.Key, property => property.Value)));
        }
    }
}
