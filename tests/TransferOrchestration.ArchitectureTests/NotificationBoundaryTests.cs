using NotificationDependencyInjection = TransferOrchestration.Notification.DependencyInjection;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence;

namespace TransferOrchestration.ArchitectureTests;

public sealed class NotificationBoundaryTests
{
    [Fact]
    public void NotificationDoesNotReferenceTransferManagementPersistence()
    {
        var forbidden = ArchitectureTestHelpers.FindForbiddenSignatureReferences(
                typeof(NotificationDependencyInjection).Assembly,
                _ => true,
                type => type.Namespace?.StartsWith("TransferOrchestration.TransferManagement.Infrastructure", StringComparison.Ordinal) == true
                    || type == typeof(TransferManagementDbContext)
                    || type.Namespace?.StartsWith("TransferOrchestration.TransferManagement.Domain", StringComparison.Ordinal) == true)
            .Select(pair => ArchitectureTestHelpers.FormatViolation(pair.Source, pair.Forbidden))
            .ToList();

        Assert.True(forbidden.Count == 0, string.Join(Environment.NewLine, forbidden));
    }

    [Fact]
    public void NotificationDoesNotReferenceTransferManagementDbContextAssemblyTypes()
    {
        var transferPersistenceTypes = ArchitectureTestHelpers.GetAssemblyTypes(typeof(TransferManagementDbContext).Assembly)
            .Where(type => type.Namespace?.StartsWith("TransferOrchestration.TransferManagement.Infrastructure.Persistence", StringComparison.Ordinal) == true)
            .ToHashSet();

        var violations = ArchitectureTestHelpers.GetAssemblyTypes(typeof(NotificationDependencyInjection).Assembly)
            .SelectMany(ArchitectureTestHelpers.ReferencedSignatureTypes)
            .Where(transferPersistenceTypes.Contains)
            .Distinct()
            .Select(type => $"Notification must not reference transfer persistence type {type.FullName}.")
            .ToList();

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }
}
