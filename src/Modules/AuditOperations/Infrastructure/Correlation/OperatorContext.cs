using TransferOrchestration.AuditOperations.Contracts;

namespace TransferOrchestration.AuditOperations.Infrastructure.Correlation;

internal sealed class OperatorContext : IOperatorContext
{
    public string? OperatorId { get; private set; }

    public void SetOperatorId(string operatorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorId);
        OperatorId = operatorId.Trim();
    }
}
