namespace TransferOrchestration.TransferManagement.Infrastructure.Persistence;

internal sealed class DailyTransferUsage
{
    public Guid SourceAccountId { get; private set; }

    public string Currency { get; private set; } = null!;

    public DateOnly UtcDay { get; private set; }

    public decimal ConsumedAmount { get; private set; }
}
