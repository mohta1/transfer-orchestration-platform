using Microsoft.Extensions.DependencyInjection;
using TransferOrchestration.TransferManagement.Application.FraudScreening;
using TransferOrchestration.TransferManagement.Application.ProcessManagement;

namespace TransferOrchestration.IntegrationTests;

internal static class FraudScreeningTestSupport
{
    internal static async Task<int> DispatchDueFraudScreeningAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default) =>
        await services.GetRequiredService<IFraudScreeningDueWorkDispatcher>()
            .DispatchDueAsync(cancellationToken);

    internal static async Task<int> DispatchDueReserveBalanceAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default) =>
        await services.GetRequiredService<ITransferProcessDueWorkDispatcher>()
            .DispatchDueAsync(cancellationToken);
}
