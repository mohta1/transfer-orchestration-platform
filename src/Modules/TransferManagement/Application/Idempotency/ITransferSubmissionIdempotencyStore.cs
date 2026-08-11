namespace TransferOrchestration.TransferManagement.Application.Idempotency;

internal interface ITransferSubmissionIdempotencyStore
{
    Task<IdempotencyClaim> TryClaimAsync(
        string idempotencyKey,
        string fingerprint,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken);

    Task LinkToTransferAsync(Guid ownerToken, Guid transferId, CancellationToken cancellationToken);

    Task CompleteAsync(
        Guid ownerToken,
        TransferSubmissionResult result,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken);
}
