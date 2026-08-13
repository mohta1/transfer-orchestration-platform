using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace TransferOrchestration.TransferManagement.Application.Observability;

internal static class OperationalTelemetry
{
    private static readonly Action<ILogger, Guid, string, string, string, Guid?, Guid?, Exception?> StateTransition =
        LoggerMessage.Define<Guid, string, string, string, Guid?, Guid?>(
            LogLevel.Information,
            new EventId(100, nameof(StateTransition)),
            "Transfer {TransferId} transitioned {PreviousState} -> {NewState} step {WorkflowStep} CorrelationId {CorrelationId} CausationId {CausationId}");

    private static readonly Action<ILogger, string, Guid, string, long, Guid?, Exception?> ExternalCallCompleted =
        LoggerMessage.Define<string, Guid, string, long, Guid?>(
            LogLevel.Information,
            new EventId(101, nameof(ExternalCallCompleted)),
            "External {Dependency} call for Transfer {TransferId} completed with outcome {Outcome} in {DurationMs}ms CorrelationId {CorrelationId}");

    private static readonly Action<ILogger, Guid, string, int, DateTimeOffset, Guid?, Exception?> RetryScheduled =
        LoggerMessage.Define<Guid, string, int, DateTimeOffset, Guid?>(
            LogLevel.Information,
            new EventId(102, nameof(RetryScheduled)),
            "Retry scheduled for Transfer {TransferId} action {NextAction} attempt {AttemptCount} due {NextAttemptAtUtc} CorrelationId {CorrelationId}");

    private static readonly Action<ILogger, int, Guid, string, string, Guid?, Exception?> ReconciliationOutcome =
        LoggerMessage.Define<int, Guid, string, string, Guid?>(
            LogLevel.Information,
            new EventId(103, nameof(ReconciliationOutcome)),
            "Reconciliation attempt {AttemptCount} for Transfer {TransferId} completed with outcome {Outcome} enquiry {EnquiryResult} CorrelationId {CorrelationId}");

    private static readonly Action<ILogger, string, Guid, string, Guid?, Exception?> ConcurrencyConflict =
        LoggerMessage.Define<string, Guid, string, Guid?>(
            LogLevel.Warning,
            new EventId(104, nameof(ConcurrencyConflict)),
            "Concurrency conflict on {EntityType} for Transfer {TransferId} during {Operation} CorrelationId {CorrelationId}");

    private static readonly Action<ILogger, string, string, Guid, string, string, Guid?, Exception?> ManualAction =
        LoggerMessage.Define<string, string, Guid, string, string, Guid?>(
            LogLevel.Information,
            new EventId(105, nameof(ManualAction)),
            "Manual action {Action} by actor {ActorId} on Transfer {TransferId} {PreviousState} -> {NewState} CorrelationId {CorrelationId}");

    private static readonly Action<ILogger, Guid, string, Guid?, Exception?> SubmissionStatusUnknown =
        LoggerMessage.Define<Guid, string, Guid?>(
            LogLevel.Warning,
            new EventId(106, nameof(SubmissionStatusUnknown)),
            "Transfer {TransferId} entered SubmissionStatusUnknown after {ExternalDependency} CorrelationId {CorrelationId}");

    private static readonly Action<ILogger, int, int, int, Exception?> StuckTransfersQueried =
        LoggerMessage.Define<int, int, int>(
            LogLevel.Information,
            new EventId(107, nameof(StuckTransfersQueried)),
            "Stuck-transfer query returned {ResultCount} items thresholdSeconds {ThresholdSeconds} maxResults {MaxResults}");

    private static readonly Action<ILogger, string, string, string, Guid?, Exception?> IdempotencyObservation =
        LoggerMessage.Define<string, string, string, Guid?>(
            LogLevel.Information,
            new EventId(108, nameof(IdempotencyObservation)),
            "Idempotency {Outcome} for key fingerprint {IdempotencyKeyFingerprint} scope {Scope} CorrelationId {CorrelationId}");

    public static void LogStateTransition(
        ILogger logger,
        Guid transferId,
        string previousState,
        string newState,
        string workflowStep,
        Guid? correlationId,
        Guid? causationId) =>
        SafeLog(() => StateTransition(logger, transferId, previousState, newState, workflowStep, correlationId, causationId, null));

    public static void LogExternalCallCompleted(
        ILogger logger,
        Guid transferId,
        string dependency,
        string outcome,
        long durationMs,
        Guid? correlationId) =>
        SafeLog(() => ExternalCallCompleted(logger, dependency, transferId, outcome, durationMs, correlationId, null));

    public static void LogRetryScheduled(
        ILogger logger,
        Guid transferId,
        string nextAction,
        int attemptCount,
        DateTimeOffset nextAttemptAtUtc,
        Guid? correlationId) =>
        SafeLog(() => RetryScheduled(logger, transferId, nextAction, attemptCount, nextAttemptAtUtc, correlationId, null));

    public static void LogReconciliationOutcome(
        ILogger logger,
        Guid transferId,
        int attemptCount,
        string outcome,
        string enquiryResult,
        Guid? correlationId) =>
        SafeLog(() => ReconciliationOutcome(logger, attemptCount, transferId, outcome, enquiryResult, correlationId, null));

    public static void LogConcurrencyConflict(
        ILogger logger,
        Guid transferId,
        string entityType,
        string operation,
        Guid? correlationId) =>
        SafeLog(() => ConcurrencyConflict(logger, entityType, transferId, operation, correlationId, null));

    public static void LogManualAction(
        ILogger logger,
        Guid transferId,
        string action,
        string actorId,
        string previousState,
        string newState,
        Guid? correlationId,
        Guid? causationId) =>
        SafeLog(() => ManualAction(logger, action, actorId, transferId, previousState, newState, correlationId, null));

    public static void LogSubmissionStatusUnknown(
        ILogger logger,
        Guid transferId,
        string externalDependency,
        Guid? correlationId) =>
        SafeLog(() => SubmissionStatusUnknown(logger, transferId, externalDependency, correlationId, null));

    public static void LogStuckTransfersQueried(
        ILogger logger,
        int resultCount,
        int thresholdSeconds,
        int maxResults,
        Exception? exception) =>
        SafeLog(() => StuckTransfersQueried(logger, resultCount, thresholdSeconds, maxResults, exception));

    public static void LogIdempotencyObservation(
        ILogger logger,
        string outcome,
        string idempotencyKeyFingerprint,
        string scope,
        Guid? correlationId) =>
        SafeLog(() => IdempotencyObservation(logger, outcome, idempotencyKeyFingerprint, scope, correlationId, null));

    public static string FingerprintAccount(Guid accountId)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(accountId.ToByteArray(), hash);
        return Convert.ToHexString(hash[..4]);
    }

    public static string FingerprintIdempotencyKey(string idempotencyKey)
    {
        var bytes = Encoding.UTF8.GetBytes(idempotencyKey);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(bytes, hash);
        return Convert.ToHexString(hash[..8]);
    }

    private static void SafeLog(Action write)
    {
        try
        {
            write();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
        }
    }
}
