using System.Reflection;
using System.Xml.Linq;

namespace TransferOrchestration.ArchitectureTests;

public sealed class ApiCompositionRootTests
{
    private static readonly string[] ModuleProjectNames =
    [
        "TransferOrchestration.TransferManagement.csproj",
        "TransferOrchestration.AccountBalance.csproj",
        "TransferOrchestration.PaymentNetwork.csproj",
        "TransferOrchestration.Reconciliation.csproj",
        "TransferOrchestration.Notification.csproj",
        "TransferOrchestration.AuditOperations.csproj",
    ];

    [Fact]
    public void ApiProjectReferencesEveryModuleProject()
    {
        var apiProjectPath = FindRepositoryFile(@"src\TransferOrchestration.Api\TransferOrchestration.Api.csproj");
        var projectReferences = XDocument.Load(apiProjectPath)
            .Descendants("ProjectReference")
            .Select(element => Path.GetFileName(element.Attribute("Include")?.Value ?? string.Empty))
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = ModuleProjectNames.Where(name => !projectReferences.Contains(name)).ToList();
        Assert.True(missing.Count == 0,
            $"The API composition root must reference every module project. Missing: {string.Join(", ", missing)}.");
    }

    [Fact]
    public void ModuleAssembliesDoNotReferenceApiAssembly()
    {
        Assembly[] moduleAssemblies =
        [
            typeof(TransferOrchestration.TransferManagement.DependencyInjection).Assembly,
            typeof(TransferOrchestration.AccountBalance.DependencyInjection).Assembly,
            typeof(TransferOrchestration.PaymentNetwork.DependencyInjection).Assembly,
            typeof(TransferOrchestration.Notification.DependencyInjection).Assembly,
            typeof(TransferOrchestration.AuditOperations.DependencyInjection).Assembly,
            Assembly.Load("TransferOrchestration.Reconciliation"),
        ];

        var violations = moduleAssemblies
            .SelectMany(assembly => assembly.GetReferencedAssemblies()
                .Where(reference => reference.Name == "TransferOrchestration.Api")
                .Select(_ => $"{assembly.GetName().Name} must not reference TransferOrchestration.Api."))
            .ToList();

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    private static string FindRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not locate repository file '{relativePath}'.");
    }
}
