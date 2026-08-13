using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TransferOrchestration.TransferManagement.Application.Observability;
using TransferOrchestration.TransferManagement.Contracts.Queries;
using TransferOrchestration.TransferManagement.Infrastructure.Operations;

namespace TransferOrchestration.TransferManagement.Application.Queries;

internal sealed class StuckTransferQueries(
    IStuckTransferQueryRepository repository,
    IOptions<StuckTransferOperationsOptions> options,
    TimeProvider timeProvider,
    ILogger<StuckTransferQueries> logger) : IStuckTransferQueries
{
    public async Task<StuckTransferQueryResult> ListAsync(
        StuckTransferQueryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var configured = options.Value;
        var maxResults = request.MaxResults ?? configured.MaxResults;
        if (maxResults < 1 || maxResults > configured.MaxResults)
        {
            throw new StuckTransferQueryValidationException(
                $"MaxResults must be between 1 and {configured.MaxResults}.");
        }

        var nowUtc = timeProvider.GetUtcNow();
        if (nowUtc.Offset != TimeSpan.Zero)
        {
            throw new InvalidOperationException("Stuck-transfer detection requires UTC time.");
        }

        var threshold = configured.StateAgeThreshold;
        var candidates = await repository.ListCandidatesAsync(
            nowUtc,
            threshold,
            maxResults,
            cancellationToken);

        var items = candidates
            .Select(candidate =>
            {
                var reference = StuckTransferClassifier.ReferenceTimestamp(
                    candidate.TransferUpdatedAtUtc,
                    candidate.ProcessUpdatedAtUtc);
                var ageSeconds = (long)(nowUtc - reference).TotalSeconds;
                return new StuckTransferItemDto(
                    candidate.TransferId,
                    candidate.TransferState.ToString(),
                    candidate.ProcessStatus.ToString(),
                    candidate.CurrentStep.ToString(),
                    candidate.NextAction.ToString(),
                    candidate.AttemptCount,
                    candidate.CreatedAtUtc,
                    reference,
                    candidate.NextAttemptAtUtc,
                    ageSeconds,
                    StuckTransferClassifier.ThresholdCrossedAtUtc(reference, threshold),
                    candidate.CorrelationId,
                    StuckTransferClassifier.ClassifyCategory(
                        candidate.TransferState,
                        candidate.ProcessStatus,
                        candidate.NextAttemptAtUtc,
                        nowUtc));
            })
            .ToList();

        OperationalTelemetry.LogStuckTransfersQueried(
            logger,
            items.Count,
            configured.StateAgeThresholdSeconds,
            maxResults,
            null);

        return new StuckTransferQueryResult(
            items,
            configured.StateAgeThresholdSeconds,
            nowUtc);
    }
}
