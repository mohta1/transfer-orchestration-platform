namespace TransferOrchestration.BuildingBlocks.Security;

public interface ICallerIdentity
{
    bool IsAuthenticated { get; }

    string? SubjectId { get; }

    Guid? AccountId { get; }

    bool IsCustomer { get; }

    bool IsOperator { get; }
}
