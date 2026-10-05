using System.Linq;
using Microsoft.CodeAnalysis;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class ConfigurationOwnership
{
    internal static bool IsWithinBinding(Compilation compilation, ISymbol? symbol) =>
        IsWithin(compilation, symbol, ConfigurationMetadataNames.BindingOwner);

    internal static bool IsWithinOptions(Compilation compilation, ISymbol? symbol) =>
        IsWithin(compilation, symbol, ConfigurationMetadataNames.OptionsOwner);

    internal static bool IsOptionsType(Compilation compilation, ITypeSymbol? type) =>
        type is not null && HasMarker(compilation, type, ConfigurationMetadataNames.OptionsOwner);

    internal static bool IsConfiguration(Compilation compilation, ITypeSymbol? type)
    {
        var contract = compilation.GetTypeByMetadataName(ConfigurationMetadataNames.Configuration);
        return contract is not null && type is not null &&
            (SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, contract) ||
             type.AllInterfaces.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, contract)));
    }

    internal static bool IsOptionsWrapper(Compilation compilation, ITypeSymbol? type) =>
        MagicRuntimeOperations.IsNativeType(compilation, type, ConfigurationMetadataNames.Options);

    private static bool IsWithin(Compilation compilation, ISymbol? symbol, string markerName)
    {
        for (var current = symbol; current is not null; current = current.ContainingSymbol)
        {
            if (HasMarker(compilation, current, markerName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasMarker(Compilation compilation, ISymbol symbol, string markerName)
    {
        var marker = compilation.GetTypeByMetadataName(markerName);
        return marker is not null && symbol.GetAttributes().Any(attribute =>
            SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, marker));
    }
}
