namespace TransferOrchestration.TransferManagement.Infrastructure.Persistence.Idempotency;

internal sealed class IdempotencyRecord
{
    public const string TransferSubmissionScope = "TransferSubmission";

    public Guid Id { get; private set; }

    public string Scope { get; private set; } = string.Empty;

    public string Key { get; private set; } = string.Empty;

    public string Fingerprint { get; private set; } = string.Empty;

    public IdempotencyRecordStatus Status { get; private set; }

    public Guid? TransferId { get; private set; }

    public string? ResultOutcome { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }
}

internal enum IdempotencyRecordStatus
{
    Processing,
    Completed
}
