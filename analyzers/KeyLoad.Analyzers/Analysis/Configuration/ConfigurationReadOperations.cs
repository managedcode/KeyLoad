using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace KeyLoad.Analyzers.CodeQuality;

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
        IInvocationOperation invocation => IsOptionsActivator(compilation, invocation) ||
            invocation.TargetMethod.Name == ConfigurationMetadataNames.Create &&
            MagicRuntimeOperations.IsType(compilation, invocation.TargetMethod.ContainingType, ConfigurationMetadataNames.StaticOptions) &&
            invocation.TargetMethod.TypeArguments.Any(type => ConfigurationOwnership.IsOptionsType(compilation, type)),
        IObjectCreationOperation { Type: INamedTypeSymbol type } =>
            (MagicRuntimeOperations.IsType(compilation, type, ConfigurationMetadataNames.OptionsFactory) ||
             MagicRuntimeOperations.IsType(compilation, type, ConfigurationMetadataNames.OptionsManager)) &&
            type.TypeArguments.Any(argument => ConfigurationOwnership.IsOptionsType(compilation, argument)),
        _ => false
    };

    private static bool IsRawMethod(Compilation compilation, IMethodSymbol method) =>
        ConfigurationOwnership.IsConfiguration(compilation, method.ContainingType) ||
        MagicRuntimeOperations.IsType(compilation, method.ContainingType, ConfigurationMetadataNames.ConfigurationBinder) ||
        MagicRuntimeOperations.IsType(compilation, method.ContainingType, ConfigurationMetadataNames.ConfigurationExtensions) ||
        MagicRuntimeOperations.IsMetadataType(compilation, method.ContainingType, ConfigurationMetadataNames.ConfigurationRootExtensions) ||
        MagicRuntimeOperations.IsMetadataType(compilation, method.ContainingType, ConfigurationMetadataNames.Environment) &&
            method.Name is ConfigurationMetadataNames.GetEnvironmentVariable or
                ConfigurationMetadataNames.GetEnvironmentVariables or ConfigurationMetadataNames.GetCommandLineArgs or
                ConfigurationMetadataNames.ExpandEnvironmentVariables;

    private static bool IsOptionsActivator(Compilation compilation, IInvocationOperation invocation) =>
        invocation.TargetMethod.Name == ConfigurationMetadataNames.CreateInstance &&
        MagicRuntimeOperations.IsMetadataType(compilation, invocation.TargetMethod.ContainingType, ConfigurationMetadataNames.Activator) &&
        (invocation.TargetMethod.TypeArguments.Any(type => ConfigurationOwnership.IsOptionsType(compilation, type)) ||
         invocation.Arguments.Any(argument => KnownOptionsType(compilation, argument.Value)));

    private static bool KnownOptionsType(Compilation compilation, IOperation operation) => operation switch
    {
        ITypeOfOperation typeOf => ConfigurationOwnership.IsOptionsType(compilation, typeOf.TypeOperand),
        IConversionOperation conversion => KnownOptionsType(compilation, conversion.Operand),
        _ => false
    };
}
