using Microsoft.Extensions.DependencyInjection;
using TransferOrchestration.PaymentNetwork.Application;
using TransferOrchestration.PaymentNetwork.Contracts;

namespace TransferOrchestration.PaymentNetwork;

public static class DependencyInjection
{
    public static IServiceCollection AddPaymentNetworkModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IPaymentNetworkProvider, DefaultPaymentNetworkProvider>();
        services.AddSingleton<IPaymentNetworkGateway, PaymentNetworkGateway>();
        return services;
    }
}
