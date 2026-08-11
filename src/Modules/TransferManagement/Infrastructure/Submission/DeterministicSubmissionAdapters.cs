using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
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
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = new NpgsqlCommand("""
            INSERT INTO transfer_management.daily_transfer_usages
                (source_account_id, currency, utc_day, consumed_amount)
            SELECT @source_account_id, @currency, @utc_day, @amount
            WHERE @amount <= @maximum
            ON CONFLICT ON CONSTRAINT "PK_daily_transfer_usages" DO UPDATE
            SET consumed_amount = daily_transfer_usages.consumed_amount + EXCLUDED.consumed_amount
            WHERE daily_transfer_usages.consumed_amount + EXCLUDED.consumed_amount <= @maximum
            RETURNING TRUE
            """, connection);
        command.Parameters.AddWithValue("source_account_id", NpgsqlDbType.Uuid, sourceAccountId);
        command.Parameters.AddWithValue("currency", NpgsqlDbType.Varchar, currency);
        command.Parameters.AddWithValue("utc_day", NpgsqlDbType.Date, utcDay);
        command.Parameters.AddWithValue("amount", NpgsqlDbType.Numeric, amount);
        command.Parameters.AddWithValue("maximum", NpgsqlDbType.Numeric, _maximum);

        var consumed = await command.ExecuteScalarAsync(cancellationToken);
        return consumed is true ? DecisionOutcome.Approved : DecisionOutcome.Rejected;
    }
}

internal sealed class AllowFraudScreening : IFraudScreening
{
    public Task<DecisionOutcome> ScreenAsync(TransferSubmissionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(DecisionOutcome.Approved);
}
