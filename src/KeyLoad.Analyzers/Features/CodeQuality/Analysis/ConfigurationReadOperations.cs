using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class ConfigurationReadOperations
{
    internal static bool IsRawRead(Compilation compilation, IOperation operation) => operation switch
    {
        IPropertyReferenceOperation property =>
            ConfigurationOwnership.IsConfiguration(compilation, property.Property.ContainingType),
        IInvocationOperation invocation => IsRawMethod(compilation, invocation.TargetMethod),
        _ => false
    };

    private static bool IsRawMethod(Compilation compilation, IMethodSymbol method) =>
        ConfigurationOwnership.IsConfiguration(compilation, method.ContainingType) ||
        MagicRuntimeOperations.IsNativeType(compilation, method.ContainingType, ConfigurationMetadataNames.ConfigurationBinder) ||
        MagicRuntimeOperations.IsNativeType(compilation, method.ContainingType, ConfigurationMetadataNames.ConfigurationExtensions) ||
        MagicRuntimeOperations.IsNativeType(compilation, method.ContainingType, ConfigurationMetadataNames.Environment) &&
            method.Name is ConfigurationMetadataNames.GetEnvironmentVariable or
                ConfigurationMetadataNames.GetEnvironmentVariables or ConfigurationMetadataNames.GetCommandLineArgs;
}
