using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class OrleansSerializerTypeGraph
{
    public static void RequirePersistentStateGraph(
        ITypeSymbol type,
        INamedTypeSymbol? persistentState,
        INamedTypeSymbol? transactionalState,
        Compilation compilation,
        INamedTypeSymbol generateSerializerAttribute,
        INamedTypeSymbol? idAttribute,
        ConcurrentDictionary<INamedTypeSymbol, byte> missingSerializers)
    {
        if (type is not INamedTypeSymbol namedType ||
            namedType.TypeArguments.Length != 1 ||
            (!SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, persistentState) &&
             !SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, transactionalState)))
        {
            return;
        }

        RequireTransportGraph(
            namedType.TypeArguments[0],
            compilation,
            generateSerializerAttribute,
            idAttribute,
            missingSerializers);
    }

    public static void RequireTransportGraph(
        ITypeSymbol type,
        Compilation compilation,
        INamedTypeSymbol generateSerializerAttribute,
        INamedTypeSymbol? idAttribute,
        ConcurrentDictionary<INamedTypeSymbol, byte> missingSerializers,
        HashSet<INamedTypeSymbol>? visited = null)
    {
        switch (type)
        {
            case IArrayTypeSymbol arrayType:
                RequireTransportGraph(
                    arrayType.ElementType,
                    compilation,
                    generateSerializerAttribute,
                    idAttribute,
                    missingSerializers,
                    visited);
                return;
            case INamedTypeSymbol namedType:
                RequireNamedTypeGraph(
                    namedType,
                    compilation,
                    generateSerializerAttribute,
                    idAttribute,
                    missingSerializers,
                    visited);
                return;
        }
    }

    private static void RequireNamedTypeGraph(
        INamedTypeSymbol namedType,
        Compilation compilation,
        INamedTypeSymbol generateSerializerAttribute,
        INamedTypeSymbol? idAttribute,
        ConcurrentDictionary<INamedTypeSymbol, byte> missingSerializers,
        HashSet<INamedTypeSymbol>? visited)
    {
        visited ??= new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var typeArgument in namedType.TypeArguments)
        {
            RequireTransportGraph(
                typeArgument,
                compilation,
                generateSerializerAttribute,
                idAttribute,
                missingSerializers,
                visited);
        }

        RequireGenerateSerializer(
            namedType,
            compilation,
            generateSerializerAttribute,
            missingSerializers);

        var definition = namedType.OriginalDefinition;
        if (idAttribute is null ||
            !SymbolEqualityComparer.Default.Equals(definition.ContainingAssembly, compilation.Assembly) ||
            !visited.Add(definition))
        {
            return;
        }

        foreach (var memberType in GetSerializedMemberTypes(namedType, idAttribute))
        {
            RequireTransportGraph(
                memberType,
                compilation,
                generateSerializerAttribute,
                idAttribute,
                missingSerializers,
                visited);
        }
    }

    public static void RequireGenerateSerializer(
        INamedTypeSymbol type,
        Compilation compilation,
        INamedTypeSymbol generateSerializerAttribute,
        ConcurrentDictionary<INamedTypeSymbol, byte> missingSerializers)
    {
        var definition = type.OriginalDefinition;
        if (!IsSerializableDataType(definition) ||
            !SymbolEqualityComparer.Default.Equals(definition.ContainingAssembly, compilation.Assembly) ||
            HasGenerateSerializer(definition, generateSerializerAttribute))
        {
            return;
        }

        missingSerializers.TryAdd(definition, 0);
    }

    public static bool DeclaresSerializedMember(
        INamedTypeSymbol type,
        INamedTypeSymbol idAttribute) => GetSerializedMemberTypes(type, idAttribute).Any();

    public static IEnumerable<ITypeSymbol> GetSerializedMemberTypes(
        INamedTypeSymbol type,
        INamedTypeSymbol idAttribute) =>
        type.GetMembers()
            .Where(member => (member is IFieldSymbol or IPropertySymbol) &&
                             member.GetAttributes().Any(attribute =>
                                 SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, idAttribute)))
            .Select(GetMemberType)
            .Where(static memberType => memberType is not null)
            .Select(static memberType => memberType!);

    public static bool IsGrainInterface(
        INamedTypeSymbol type,
        INamedTypeSymbol grainInterface) =>
        type.AllInterfaces.Any(candidate =>
            SymbolEqualityComparer.Default.Equals(candidate, grainInterface));

    public static bool IsSerializableDataType(INamedTypeSymbol type) =>
        type.TypeKind is TypeKind.Class or TypeKind.Struct &&
        !type.IsStatic &&
        !type.IsImplicitlyDeclared;

    public static ITypeSymbol? GetMemberType(ISymbol member) =>
        member switch
        {
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            _ => null
        };

    private static bool HasGenerateSerializer(
        INamedTypeSymbol type,
        INamedTypeSymbol generateSerializerAttribute) =>
        type.GetAttributes().Any(attribute =>
            SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, generateSerializerAttribute));
}
