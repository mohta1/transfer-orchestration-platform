using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using TransferOrchestration.TransferManagement.Application.Idempotency;
using TransferOrchestration.TransferManagement.Application.Submission;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence;

namespace TransferOrchestration.TransferManagement.Infrastructure.Submission;

internal sealed class AllowCustomerAuthorization : ICustomerAuthorization
{
    public Task<DecisionOutcome> IsAuthorizedAsync(Guid sourceAccountId, CancellationToken cancellationToken) =>
        Task.FromResult(DecisionOutcome.Approved);
}

internal sealed class ConfiguredDailyTransferLimit(
    IOptions<SubmissionPolicyOptions> options,
    TransferManagementDbContext context) : IDailyTransferLimit
{
    private readonly decimal _maximum = options.Value.MaximumDailyTransferAmount > 0
        ? options.Value.MaximumDailyTransferAmount
        : throw new InvalidOperationException("TransferSubmission:MaximumDailyTransferAmount must be greater than zero.");

    public async Task<DecisionOutcome> TryConsumeAsync(Guid sourceAccountId, decimal amount, string currency, DateOnly utcDay, CancellationToken cancellationToken)
    {
        var affected = await context.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO transfer_management.daily_transfer_usages
                (source_account_id, currency, utc_day, consumed_amount)
            SELECT {{sourceAccountId}}, {{currency}}, {{utcDay}}, {{amount}}
            WHERE {{amount}} <= {{_maximum}}
            ON CONFLICT (source_account_id, currency, utc_day) DO UPDATE
            SET consumed_amount = daily_transfer_usages.consumed_amount + EXCLUDED.consumed_amount
            WHERE daily_transfer_usages.consumed_amount + EXCLUDED.consumed_amount <= {{_maximum}}
            """, cancellationToken);
        return affected == 1 && amount <= _maximum ? DecisionOutcome.Approved : DecisionOutcome.Rejected;
    }
}

internal sealed class AllowFraudScreening : IFraudScreening
{
    public Task<DecisionOutcome> ScreenAsync(TransferSubmissionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(DecisionOutcome.Approved);
}
