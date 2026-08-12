using TransferOrchestration.AccountBalance.Infrastructure.Persistence;

namespace TransferOrchestration.ArchitectureTests;

public sealed class TransferManagementAccountBalanceInfrastructureTests
{
    [Fact]
    public void TransferManagementDoesNotReferenceAccountBalanceInfrastructureOrDbContext()
    {
        var forbidden = ArchitectureTestHelpers.FindForbiddenSignatureReferences(
                typeof(TransferOrchestration.TransferManagement.DependencyInjection).Assembly,
                _ => true,
                type => type.Namespace?.StartsWith("TransferOrchestration.AccountBalance.Infrastructure", StringComparison.Ordinal) == true
                    || type == typeof(AccountBalanceDbContext)
                    || type.Namespace?.StartsWith("TransferOrchestration.AccountBalance.Domain", StringComparison.Ordinal) == true)
            .Select(pair => ArchitectureTestHelpers.FormatViolation(pair.Source, pair.Forbidden))
            .ToList();

        Assert.True(forbidden.Count == 0, string.Join(Environment.NewLine, forbidden));
    }
}
