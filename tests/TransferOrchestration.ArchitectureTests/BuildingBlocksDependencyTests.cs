using TransferOrchestration.BuildingBlocks.Domain;

namespace TransferOrchestration.ArchitectureTests;

public sealed class BuildingBlocksDependencyTests
{
    private static readonly string[] ForbiddenAssemblyPrefixes =
    [
        "TransferOrchestration.TransferManagement",
        "TransferOrchestration.AccountBalance",
        "TransferOrchestration.PaymentNetwork",
        "TransferOrchestration.Notification",
        "TransferOrchestration.Reconciliation",
        "TransferOrchestration.AuditOperations",
        "TransferOrchestration.Api",
    ];

    [Fact]
    public void BuildingBlocksDoesNotReferenceModuleAssemblies()
    {
        var references = typeof(Entity<>).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToList();

        var forbidden = references
            .Where(name => ForbiddenAssemblyPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();

        Assert.True(forbidden.Count == 0,
            $"BuildingBlocks must remain dependency-light and must not reference module assemblies: {string.Join(", ", forbidden)}.");
    }

    [Fact]
    public void BuildingBlocksDoesNotReferenceEntityFrameworkOrNpgsqlPackages()
    {
        var forbiddenPackages = typeof(Entity<>).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                || name.StartsWith("Npgsql", StringComparison.Ordinal))
            .ToList();

        Assert.True(forbiddenPackages.Count == 0,
            $"BuildingBlocks must not reference persistence packages: {string.Join(", ", forbiddenPackages)}.");
    }
}
