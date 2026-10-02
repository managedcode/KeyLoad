using System.Collections.Concurrent;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal sealed class OrleansSerializerAnalysis(
    Compilation compilation,
    INamedTypeSymbol generateSerializerAttribute,
    INamedTypeSymbol? idAttribute,
    INamedTypeSymbol? grainInterface,
    INamedTypeSymbol? persistentState,
    INamedTypeSymbol? transactionalState,
    ConcurrentDictionary<INamedTypeSymbol, byte> missingSerializers)
{
    public void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        if (context.Symbol is not INamedTypeSymbol type)
        {
            return;
        }

        AnalyzeSerializedType(type);
        AnalyzeGrainInterface(type);
        AnalyzePersistentStateConstructorParameters(type);
        AnalyzePersistentStateMembers(type);
    }

    private void AnalyzeSerializedType(INamedTypeSymbol type)
    {
        if (!OrleansSerializerTypeGraph.IsSerializableDataType(type) ||
            idAttribute is null ||
            !OrleansSerializerTypeGraph.DeclaresSerializedMember(type, idAttribute))
        {
            return;
        }

        OrleansSerializerTypeGraph.RequireGenerateSerializer(
            type,
            compilation,
            generateSerializerAttribute,
            missingSerializers);
        foreach (var memberType in OrleansSerializerTypeGraph.GetSerializedMemberTypes(type, idAttribute))
        {
            OrleansSerializerTypeGraph.RequireTransportGraph(
                memberType,
                compilation,
                generateSerializerAttribute,
                idAttribute,
                missingSerializers);
        }
    }

    private void AnalyzeGrainInterface(INamedTypeSymbol type)
    {
        if (grainInterface is null ||
            type.TypeKind != TypeKind.Interface ||
            !OrleansSerializerTypeGraph.IsGrainInterface(type, grainInterface))
        {
            return;
        }

        foreach (var method in type.GetMembers().OfType<IMethodSymbol>()
                     .Where(static method => method.MethodKind == MethodKind.Ordinary))
        {
            OrleansSerializerTypeGraph.RequireTransportGraph(
                method.ReturnType,
                compilation,
                generateSerializerAttribute,
                idAttribute,
                missingSerializers);
            AnalyzeMethodParameters(method);
        }
    }

    private void AnalyzeMethodParameters(IMethodSymbol method)
    {
        foreach (var parameter in method.Parameters)
        {
            OrleansSerializerTypeGraph.RequireTransportGraph(
                parameter.Type,
                compilation,
                generateSerializerAttribute,
                idAttribute,
                missingSerializers);
        }
    }

    private void AnalyzePersistentStateConstructorParameters(INamedTypeSymbol type)
    {
        foreach (var constructor in type.InstanceConstructors)
        {
            foreach (var parameter in constructor.Parameters)
            {
                AnalyzePersistentStateType(parameter.Type);
            }
        }
    }

    private void AnalyzePersistentStateMembers(INamedTypeSymbol type)
    {
        foreach (var memberType in type.GetMembers()
                     .Select(OrleansSerializerTypeGraph.GetMemberType)
                     .Where(static memberType => memberType is not null))
        {
            AnalyzePersistentStateType(memberType!);
        }
    }

    private void AnalyzePersistentStateType(ITypeSymbol type) =>
        OrleansSerializerTypeGraph.RequirePersistentStateGraph(
            type,
            persistentState,
            transactionalState,
            compilation,
            generateSerializerAttribute,
            idAttribute,
            missingSerializers);
}
