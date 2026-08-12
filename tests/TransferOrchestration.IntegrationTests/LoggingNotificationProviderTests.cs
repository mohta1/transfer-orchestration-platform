using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;
using TransferOrchestration.Notification.Contracts;
using TransferOrchestration.Notification.Infrastructure;
using TransferOrchestration.TransferManagement.Contracts.IntegrationEvents;

namespace TransferOrchestration.IntegrationTests;

public sealed class LoggingNotificationProviderTests
{
    [Fact]
    public async Task CacheIsBoundedAndExpiresWithoutSleeping()
    {
        var clock = new MutableTimeProvider();
        var provider = CreateProvider(clock, capacity: 2, retentionSeconds: 10);
        await NotifyAsync(provider, Guid.NewGuid());
        await NotifyAsync(provider, Guid.NewGuid());
        await NotifyAsync(provider, Guid.NewGuid());
        Assert.Equal(2, provider.RetainedCount);
        clock.Advance(TimeSpan.FromSeconds(11));
        await NotifyAsync(provider, Guid.NewGuid());
        Assert.Equal(1, provider.RetainedCount);
    }

    [Fact]
    public async Task ConcurrentSameKeyIsRetainedOnce()
    {
        var provider = CreateProvider(new MutableTimeProvider(), 10, 10);
        var id = Guid.NewGuid();
        await Task.WhenAll(NotifyAsync(provider, id), NotifyAsync(provider, id));
        Assert.Equal(1, provider.RetainedCount);
    }

    [Fact]
    public async Task FailedEffectDoesNotConsumeKey()
    {
        var logger = new ThrowOnceLogger();
        var provider = new LoggingNotificationProvider(logger,
            Options.Create(new LoggingNotificationProviderOptions()), new MutableTimeProvider());
        await Assert.ThrowsAsync<InvalidOperationException>(() => NotifyAsync(provider, Guid.NewGuid()));
        Assert.Equal(0, provider.RetainedCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidCapacityFailsValidation(int capacity)
    {
        var options = new LoggingNotificationProviderOptions { Capacity = capacity };
        Assert.False(Validator.TryValidateObject(options, new ValidationContext(options), [], true));
    }

    private static LoggingNotificationProvider CreateProvider(TimeProvider clock, int capacity, int retentionSeconds) =>
        new(NullLogger<LoggingNotificationProvider>.Instance,
            Options.Create(new LoggingNotificationProviderOptions { Capacity = capacity, RetentionSeconds = retentionSeconds }), clock);

    private static Task NotifyAsync(LoggingNotificationProvider provider, Guid id)
    {
        var integrationEvent = new TransferCompletedIntegrationEvent(id, Guid.NewGuid(), DateTimeOffset.UtcNow, null);
        return provider.NotifyTransferCompletedAsync(new NotificationIdempotencyKey("test", id), integrationEvent, default);
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan value) => _now += value;
    }

    private sealed class ThrowOnceLogger : ILogger<LoggingNotificationProvider>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => throw new InvalidOperationException();
    }
}
