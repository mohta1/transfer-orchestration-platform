namespace TransferOrchestration.ArchitectureTests;

public sealed class DomainLayerDependencyTests
{
    [Fact]
    public void TransferManagementDomainDoesNotReferenceInfrastructureTypes()
    {
        var violations = ArchitectureTestHelpers.FindForbiddenDependencies(
                ArchitectureTestHelpers.GetAssemblyTypes(typeof(TransferOrchestration.TransferManagement.DependencyInjection).Assembly)
                    .Where(ArchitectureTestHelpers.IsDomainType),
                referenced => ArchitectureTestHelpers.IsInfrastructureType(referenced)
                    || referenced.Namespace?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true
                    || referenced.Namespace?.StartsWith("Npgsql", StringComparison.Ordinal) == true)
            .Select(pair => ArchitectureTestHelpers.FormatViolation(pair.Source, pair.Forbidden))
            .ToList();

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void AccountBalanceDomainDoesNotReferenceInfrastructureTypes()
    {
        var violations = ArchitectureTestHelpers.FindForbiddenDependencies(
                ArchitectureTestHelpers.GetAssemblyTypes(typeof(TransferOrchestration.AccountBalance.DependencyInjection).Assembly)
                    .Where(ArchitectureTestHelpers.IsDomainType),
                referenced => ArchitectureTestHelpers.IsInfrastructureType(referenced)
                    || referenced.Namespace?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true
                    || referenced.Namespace?.StartsWith("Npgsql", StringComparison.Ordinal) == true)
            .Select(pair => ArchitectureTestHelpers.FormatViolation(pair.Source, pair.Forbidden))
            .ToList();

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }
}
