using TransferOrchestration.TransferManagement;

namespace TransferOrchestration.ArchitectureTests;

public sealed class AccountBalanceBoundaryTests
{
    [Fact]
    public void TransferManagementSignaturesReferenceOnlyAccountBalanceContracts()
    {
        var forbidden = ArchitectureTestHelpers.GetAssemblyTypes(typeof(DependencyInjection).Assembly)
            .SelectMany(ArchitectureTestHelpers.ReferencedSignatureTypes)
            .Where(type => type.Namespace?.StartsWith(
                "TransferOrchestration.AccountBalance.",
                StringComparison.Ordinal) == true)
            .Where(type => type.Namespace != "TransferOrchestration.AccountBalance.Contracts")
            .Distinct()
            .ToList();

        Assert.Empty(forbidden);
    }
}
