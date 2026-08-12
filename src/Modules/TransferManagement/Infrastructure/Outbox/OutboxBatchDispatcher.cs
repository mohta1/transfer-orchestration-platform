using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TransferOrchestration.TransferManagement.Contracts.IntegrationEvents;

namespace TransferOrchestration.TransferManagement.Infrastructure.Outbox;

internal sealed class OutboxBatchDispatcher(
    IOutboxStore store,
    IIntegrationEventDispatcher dispatcher,
    IOptions<OutboxOptions> options,
    ILogger<OutboxBatchDispatcher> logger)
{
    private static readonly Action<ILogger, string, Guid, Guid, Guid?, int, string, Exception?> DispatchResult =
        LoggerMessage.Define<string, Guid, Guid, Guid?, int, string>(LogLevel.Information,
            new EventId(1, nameof(DispatchResult)),
            "Outbox dispatch {Status}: {MessageId} {TransferId} correlation {CorrelationId} attempt {Attempt} worker {WorkerInstanceId}");
    private static readonly Action<ILogger, string, Guid, Guid, Guid?, int, string, Exception?> DispatchFailure =
        LoggerMessage.Define<string, Guid, Guid, Guid?, int, string>(LogLevel.Warning,
            new EventId(2, nameof(DispatchFailure)),
            "Outbox dispatch {Status}: {MessageId} {TransferId} correlation {CorrelationId} attempt {Attempt} worker {WorkerInstanceId}");
    private readonly OutboxOptions _options = options.Value;

    public async Task<int> DispatchBatchAsync(string workerId, CancellationToken cancellationToken)
    {
        var claimed = 0;
        var processed = 0;
        while (claimed < _options.BatchSize)
        {
            var claim = await store.ClaimOneAsync(workerId, _options.LeaseDuration, cancellationToken);
            if (claim is null) break;
            claimed++;
            var renewed = await store.RenewBeforeDispatchAsync(claim, workerId, _options.LeaseDuration, cancellationToken);
            if (renewed is null) continue;
            await DispatchOneAsync(renewed, workerId, cancellationToken);
            processed++;
        }
        return processed;
    }

    private async Task DispatchOneAsync(OutboxClaim claim, string workerId, CancellationToken token)
    {
        try
        {
            if (claim.Type != TransferCompletedIntegrationEvent.EventType)
                throw new PermanentOutboxException("Unsupported integration event type.");

            var integrationEvent = JsonSerializer.Deserialize<TransferCompletedIntegrationEvent>(claim.Payload)
                ?? throw new PermanentOutboxException("Integration event payload is null.");
            if (integrationEvent.MessageId != claim.MessageId || integrationEvent.TransferId != claim.TransferId
                || integrationEvent.CorrelationId != claim.CorrelationId)
                throw new PermanentOutboxException("Integration event envelope does not match its payload.");

            await dispatcher.DispatchAsync(integrationEvent, token);
            var updated = await store.MarkPublishedAsync(claim, workerId, token);
            DispatchResult(logger,
                updated == 1 ? "Published" : "LeaseLost", claim.MessageId, claim.TransferId, claim.CorrelationId,
                claim.Attempts + 1, workerId, null);
        }
        catch (Exception exception) when (exception is JsonException or PermanentOutboxException)
        {
            await MarkFailureAsync(claim, workerId, exception.Message, true, token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !token.IsCancellationRequested)
        {
            await MarkFailureAsync(claim, workerId, exception.Message, false, token);
        }
    }

    private async Task MarkFailureAsync(OutboxClaim claim, string workerId, string error, bool permanent, CancellationToken token)
    {
        var attempt = claim.Attempts + 1;
        var deadLetter = permanent || attempt >= _options.MaxAttempts;
        var retryDelay = deadLetter ? TimeSpan.Zero : RetryDelay(claim.MessageId, attempt);
        var safeError = error.Length <= 1000 ? error : error[..1000];
        var updated = await store.MarkFailureAsync(claim, workerId, retryDelay, safeError, deadLetter, token);
        DispatchFailure(logger,
            updated == 0 ? "LeaseLost" : deadLetter ? "DeadLetter" : "RetryScheduled",
            claim.MessageId, claim.TransferId, claim.CorrelationId, attempt, workerId, null);
    }

    internal TimeSpan RetryDelay(Guid messageId, int attempt)
    {
        var exponent = Math.Min(attempt - 1, 30);
        var baseTicks = Math.Min(
            _options.InitialRetryDelay.Ticks * Math.Pow(2, exponent),
            _options.MaxRetryDelay.Ticks);
        Span<byte> input = stackalloc byte[20];
        messageId.TryWriteBytes(input);
        BitConverter.TryWriteBytes(input[16..], attempt);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        var fraction = BitConverter.ToUInt32(hash) / (double)uint.MaxValue;
        var jitteredTicks = baseTicks * (0.75 + (fraction * 0.5));
        return TimeSpan.FromTicks((long)Math.Min(jitteredTicks, _options.MaxRetryDelay.Ticks));
    }

    private sealed class PermanentOutboxException(string message) : Exception(message);
}
