using Microsoft.Extensions.Logging;
using TransferOrchestration.TransferManagement.Application.Observability;

namespace TransferOrchestration.IntegrationTests;

public sealed class ObservabilityTelemetryTests
{
    [Fact]
    public void IdempotencyFingerprintNeverContainsRawKey()
    {
        const string rawKey = "customer-idempotency-key-12345";
        var fingerprint = OperationalTelemetry.FingerprintIdempotencyKey(rawKey);
        Assert.DoesNotContain(rawKey, fingerprint, StringComparison.Ordinal);
        Assert.Equal(16, fingerprint.Length);
    }

    [Fact]
    public void AccountFingerprintNeverContainsRawGuid()
    {
        var accountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var fingerprint = OperationalTelemetry.FingerprintAccount(accountId);
        Assert.DoesNotContain(accountId.ToString("D"), fingerprint, StringComparison.Ordinal);
        Assert.Equal(8, fingerprint.Length);
    }

    [Fact]
    public void StructuredLogTemplatesIncludeRequiredFields()
    {
        var sink = new List<string>();
        using var provider = new TestLoggerProvider(sink);
        var logger = provider.CreateLogger("observability-test");

        OperationalTelemetry.LogStateTransition(
            logger,
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "Submitted",
            "PendingFraudScreening",
            "Submission",
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
        OperationalTelemetry.LogExternalCallCompleted(
            logger,
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "FraudScreening",
            "Approved",
            12,
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        OperationalTelemetry.LogStuckTransfersQueried(logger, 2, 600, 50, null);

        var combined = string.Join('\n', sink);
        Assert.Contains("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cccccccc-cccc-cccc-cccc-cccccccccccc", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(" in 12ms ", combined, StringComparison.Ordinal);
        Assert.Contains("Stuck-transfer query returned", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("customer-idempotency-key", combined, StringComparison.Ordinal);
    }

    [Fact]
    public void LoggingFailureDoesNotThrow()
    {
        var logger = new ThrowingLogger();
        var exception = Record.Exception(() =>
            OperationalTelemetry.LogExternalCallCompleted(
                logger,
                Guid.NewGuid(),
                "PaymentNetwork",
                "Accepted",
                1,
                Guid.NewGuid()));
        Assert.Null(exception);
    }

    private sealed class TestLoggerProvider(List<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new TestLogger(sink);

        public void Dispose()
        {
        }
    }

    private sealed class TestLogger(List<string> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            sink.Add(formatter(state, exception));
    }

    private sealed class ThrowingLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            throw new InvalidOperationException("Simulated logging failure.");
    }
}
