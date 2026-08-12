using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;
using TransferOrchestration.AuditOperations.Contracts;
using TransferOrchestration.AuditOperations.Domain;
using TransferOrchestration.AuditOperations.Infrastructure.Persistence;

namespace TransferOrchestration.AuditOperations.Application;

internal sealed class OperationsAuditWriter(AuditOperationsDbContext context) : IOperationsAuditWriter
{
    public Task<OperationsAuditEntry?> FindByCommandIdAsync(
        string commandId,
        CancellationToken cancellationToken) =>
        context.OperationsAuditRecords.AsNoTracking()
            .Where(record => record.CommandId == commandId)
            .Select(record => new OperationsAuditEntry(
                record.CommandId,
                record.ActorId,
                record.Action,
                record.TransferId,
                record.PreviousState,
                record.NewState,
                record.Reason,
                record.CorrelationId,
                record.CausationId,
                record.OccurredAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

    public void Stage(OperationsAuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        context.OperationsAuditRecords.Add(OperationsAuditRecord.Create(
            entry.CommandId,
            entry.ActorId,
            entry.Action,
            entry.TransferId,
            entry.PreviousState,
            entry.NewState,
            entry.Reason,
            entry.CorrelationId,
            entry.CausationId,
            entry.OccurredAtUtc));
    }

    public Task SaveStagedAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);

    public void Enlist(object dbContextTransaction)
    {
        ArgumentNullException.ThrowIfNull(dbContextTransaction);
        if (dbContextTransaction is not IDbContextTransaction transaction)
        {
            throw new ArgumentException(
                "The enlisted transaction must be an EF Core database transaction.",
                nameof(dbContextTransaction));
        }

        var dbTransaction = transaction.GetDbTransaction();
        var dbConnection = dbTransaction.Connection
            ?? throw new InvalidOperationException("The enlisted transaction must expose its connection.");

        context.Database.CloseConnection();
        context.Database.SetDbConnection((DbConnection)dbConnection);
        context.Database.UseTransaction(dbTransaction);
    }
}
