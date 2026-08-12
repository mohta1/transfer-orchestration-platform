using TransferOrchestration.AuditOperations.Contracts;

namespace TransferOrchestration.AuditOperations.Infrastructure.Correlation;

internal sealed class CorrelationContext : ICorrelationContext
{
    public Guid CorrelationId { get; private set; }

    public Guid? CausationId { get; private set; }

    public void SetCorrelationId(Guid correlationId)
    {
        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("Correlation identifier cannot be empty.", nameof(correlationId));
        }

        CorrelationId = correlationId;
    }

    public void SetCausationId(Guid? causationId)
    {
        if (causationId == Guid.Empty)
        {
            throw new ArgumentException("Causation identifier cannot be empty.", nameof(causationId));
        }

        CausationId = causationId;
    }
}
