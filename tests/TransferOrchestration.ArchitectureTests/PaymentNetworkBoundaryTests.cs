using TransferOrchestration.PaymentNetwork.Contracts;
using TransferOrchestration.TransferManagement;

namespace TransferOrchestration.ArchitectureTests;

public sealed class PaymentNetworkBoundaryTests
{
    [Fact]
    public void TransferManagementSignaturesReferenceOnlyPaymentNetworkContracts()
    {
        var forbidden = ArchitectureTestHelpers.GetAssemblyTypes(typeof(DependencyInjection).Assembly)
            .SelectMany(ArchitectureTestHelpers.ReferencedSignatureTypes)
            .Where(type => type.Namespace?.StartsWith("TransferOrchestration.PaymentNetwork.", StringComparison.Ordinal) == true)
            .Where(type => type.Namespace != typeof(IPaymentNetworkGateway).Namespace)
            .Distinct()
            .ToList();

        Assert.Empty(forbidden);
    }

    [Fact]
    public void PaymentNetworkDoesNotReferenceTransferManagementAssembly()
    {
        var references = typeof(IPaymentNetworkGateway).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, reference =>
            reference.Name == "TransferOrchestration.TransferManagement");
    }
}
