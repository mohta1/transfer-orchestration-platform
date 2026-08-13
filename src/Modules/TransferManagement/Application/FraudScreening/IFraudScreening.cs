namespace TransferOrchestration.TransferManagement.Application.FraudScreening;

internal interface IFraudScreening
{
    Task<FraudScreeningResult> ScreenAsync(FraudScreeningRequest request, CancellationToken cancellationToken);
}
