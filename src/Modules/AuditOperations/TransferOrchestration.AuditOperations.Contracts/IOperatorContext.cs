namespace TransferOrchestration.AuditOperations.Contracts;

public interface IOperatorContext
{
    string? OperatorId { get; }

    void SetOperatorId(string operatorId);
}
