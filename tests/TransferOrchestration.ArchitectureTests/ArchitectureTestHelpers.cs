using System.Reflection;

namespace TransferOrchestration.ArchitectureTests;

internal static class ArchitectureTestHelpers
{
    internal static IEnumerable<Type> GetAssemblyTypes(Assembly assembly) => assembly.GetTypes();

    internal static IEnumerable<Type> ReferencedSignatureTypes(Type type)
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

    internal static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;
        foreach (var argument in type.GetGenericArguments())
        foreach (var nested in Flatten(argument)) yield return nested;
    }

    internal static IEnumerable<(Type Source, Type Forbidden)> FindForbiddenSignatureReferences(
        Assembly assembly,
        Func<Type, bool> sourceFilter,
        Func<Type, bool> forbiddenFilter)
    {
        foreach (var source in GetAssemblyTypes(assembly).Where(sourceFilter))
        foreach (var referenced in ReferencedSignatureTypes(source).Where(forbiddenFilter).Distinct())
            yield return (source, referenced);
    }

    internal static IEnumerable<(Type Source, Type Forbidden)> FindForbiddenDependencies(
        IEnumerable<Type> sourceTypes,
        Func<Type, bool> forbiddenFilter)
    {
        foreach (var source in sourceTypes)
        foreach (var referenced in ReferencedSignatureTypes(source).Where(forbiddenFilter).Distinct())
            yield return (source, referenced);
    }

    internal static bool IsDomainType(Type type) =>
        type.Namespace?.Contains(".Domain.", StringComparison.Ordinal) == true;

    internal static bool IsInfrastructureType(Type type) =>
        type.Namespace?.Contains(".Infrastructure.", StringComparison.Ordinal) == true;

    internal static string FormatViolation(Type source, Type forbidden) =>
        $"{source.FullName} must not reference {forbidden.FullName}.";
}
