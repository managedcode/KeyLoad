using System.Linq;
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

    internal static bool IsOptionsFactory(Compilation compilation, IOperation operation) => operation switch
    {
        IInvocationOperation invocation => invocation.TargetMethod.Name == ConfigurationMetadataNames.Create &&
            MagicRuntimeOperations.IsNativeType(compilation, invocation.TargetMethod.ContainingType, ConfigurationMetadataNames.StaticOptions) &&
            invocation.TargetMethod.TypeArguments.Any(type => ConfigurationOwnership.IsOptionsType(compilation, type)),
        IObjectCreationOperation { Type: INamedTypeSymbol type } =>
            (MagicRuntimeOperations.IsNativeType(compilation, type, ConfigurationMetadataNames.OptionsFactory) ||
             MagicRuntimeOperations.IsNativeType(compilation, type, ConfigurationMetadataNames.OptionsManager)) &&
            type.TypeArguments.Any(argument => ConfigurationOwnership.IsOptionsType(compilation, argument)),
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
