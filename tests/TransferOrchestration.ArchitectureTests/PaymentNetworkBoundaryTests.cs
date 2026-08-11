using System.Reflection;
using TransferOrchestration.PaymentNetwork.Contracts;
using TransferOrchestration.TransferManagement;

namespace TransferOrchestration.ArchitectureTests;

public sealed class PaymentNetworkBoundaryTests
{
    [Fact]
    public void TransferManagementSignaturesReferenceOnlyPaymentNetworkContracts()
    {
        var forbidden = typeof(DependencyInjection).Assembly
            .GetTypes()
            .SelectMany(ReferencedSignatureTypes)
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

    private static IEnumerable<Type> ReferencedSignatureTypes(Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (var field in type.GetFields(flags))
        foreach (var referenced in Flatten(field.FieldType)) yield return referenced;
        foreach (var property in type.GetProperties(flags))
        foreach (var referenced in Flatten(property.PropertyType)) yield return referenced;
        foreach (var method in type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags)))
        {
            if (method is MethodInfo methodInfo)
            foreach (var referenced in Flatten(methodInfo.ReturnType)) yield return referenced;
            foreach (var parameter in method.GetParameters())
            foreach (var referenced in Flatten(parameter.ParameterType)) yield return referenced;
        }
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;
        foreach (var argument in type.GetGenericArguments())
        foreach (var nested in Flatten(argument)) yield return nested;
    }
}
