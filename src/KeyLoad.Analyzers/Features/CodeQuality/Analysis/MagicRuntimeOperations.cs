using System.Linq;
using Microsoft.CodeAnalysis;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class MagicRuntimeOperations
{
    internal static bool IsNativeType(Compilation compilation, ITypeSymbol? type, string metadataName) =>
        type is not null && SymbolEqualityComparer.Default.Equals(type.OriginalDefinition,
            compilation.GetTypeByMetadataName(metadataName));

    internal static bool IsNativeMetadataType(Compilation compilation, ITypeSymbol? type, string metadataName) =>
        IsNativeType(compilation, type, metadataName) &&
        type!.Locations.All(static location => location.IsInMetadata);

    internal static bool IsNumeric(ITypeSymbol? type) => type?.SpecialType is
        SpecialType.System_Byte or SpecialType.System_SByte or
        SpecialType.System_Int16 or SpecialType.System_UInt16 or
        SpecialType.System_Int32 or SpecialType.System_UInt32 or
        SpecialType.System_Int64 or SpecialType.System_UInt64 or
        SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal;
}
