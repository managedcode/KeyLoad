using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class ImplementationTermSymbols
{
    internal static ISymbol? Resolve(SemanticModel model, SyntaxNode? node, CancellationToken cancellationToken)
    {
        if (node is null)
        {
            return null;
        }
        if (node is SimpleNameSyntax or MemberAccessExpressionSyntax)
        {
            return model.GetSymbolInfo(node, cancellationToken).Symbol;
        }
        if (node is FieldDeclarationSyntax field && field.Declaration.Variables.Count == 1)
        {
            return model.GetDeclaredSymbol(field.Declaration.Variables[0], cancellationToken);
        }
        return node is MemberDeclarationSyntax or VariableDeclaratorSyntax or ParameterSyntax or TypeParameterSyntax
            ? model.GetDeclaredSymbol(node, cancellationToken) : null;
    }

    internal static bool Native(ISymbol? symbol) => symbol is not null &&
        symbol.Locations.All(static location => !location.IsInSource) &&
        ImplementationTermCatalog.NativeNamespace(symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty);

    internal static bool HasPath(ISymbol? symbol, string segment) => symbol is not null &&
        (symbol is INamedTypeSymbol { DeclaringSyntaxReferences.Length: > 0 } type
            ? type.DeclaringSyntaxReferences.All(reference => PathContains(reference.SyntaxTree.FilePath, segment))
            : symbol.DeclaringSyntaxReferences.Any(reference => PathContains(reference.SyntaxTree.FilePath, segment)) ||
              symbol.ContainingType is { DeclaringSyntaxReferences.Length: > 0 } owner &&
              owner.DeclaringSyntaxReferences.All(reference => PathContains(reference.SyntaxTree.FilePath, segment)));

    internal static bool PathContains(string path, string segment) =>
        path.Replace('\\', '/').Contains(segment, StringComparison.Ordinal);

    internal static ITypeSymbol? ValueType(ISymbol? symbol) => symbol switch
    {
        ILocalSymbol local => local.Type,
        IParameterSymbol parameter => parameter.Type,
        IFieldSymbol field => field.Type,
        IPropertySymbol property => property.Type,
        IMethodSymbol method => method.ReturnType,
        ITypeSymbol type => type,
        _ => null
    };

    internal static bool TestOwner(ISymbol? symbol)
    {
        if (HasPath(symbol, ImplementationTermCatalog.TestPath) && HasPath(symbol, ImplementationTermCatalog.TestOwnerPath))
        {
            return true;
        }
        var owner = ValueType(symbol) ?? symbol?.ContainingType;
        return TypeMeans(owner as ITypeSymbol, static type =>
            HasPath(type, ImplementationTermCatalog.TestPath) && HasPath(type, ImplementationTermCatalog.TestOwnerPath),
            new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default)) ||
            TypeMeans(symbol?.ContainingType, static type =>
                HasPath(type, ImplementationTermCatalog.TestPath) && HasPath(type, ImplementationTermCatalog.TestOwnerPath),
                new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default));
    }

    internal static bool TimeSeries(ISymbol? symbol, Compilation compilation)
    {
        if (symbol is IFieldSymbol or IPropertySymbol && symbol.ContainingAssembly.Name == "KeyLoad.BenchmarkScenarios" &&
            Array.Exists(ImplementationTermCatalog.DataControlMembers, member => member == symbol.ToDisplayString()) ||
            symbol is IFieldSymbol { Name: "SeriesRead" or "SeriesAppend", ContainingType: { } capability } &&
            capability.ToDisplayString() == "KeyLoad.Capability" &&
            capability.ContainingAssembly.Name == ImplementationTermCatalog.DataAssembly ||
            symbol is IFieldSymbol { IsConst: true, Type.SpecialType: SpecialType.System_String, ContainingType: { } family } &&
            family.ToDisplayString() == ImplementationTermCatalog.DataRecordFamiliesType &&
            family.ContainingAssembly.Name == ImplementationTermCatalog.DataCoreAssembly &&
            Array.Exists(ImplementationTermCatalog.DataRecordFamilyFields, name => name == symbol.Name) ||
            symbol is IFieldSymbol { ContainingType: { TypeKind: TypeKind.Enum } runtime } &&
            runtime.ContainingAssembly.Name == ImplementationTermCatalog.DataRuntimeAssembly &&
            (runtime.ToDisplayString() == ImplementationTermCatalog.DataReadKindType &&
             Array.Exists(ImplementationTermCatalog.DataReadKinds, name => name == symbol.Name) ||
             runtime.ToDisplayString() == ImplementationTermCatalog.DataRequestPhaseType &&
             Array.Exists(ImplementationTermCatalog.DataRequestPhases, name => name == symbol.Name)) ||
            symbol is IFieldSymbol { IsConst: true, Type.SpecialType: SpecialType.System_String, ContainingType: { } descriptions } &&
            descriptions.ContainingAssembly.Name == ImplementationTermCatalog.DataServerAssembly &&
            descriptions.ToDisplayString() == ImplementationTermCatalog.DataToolDescriptionsType &&
            Array.Exists(ImplementationTermCatalog.DataToolDescriptionFields, name => name == symbol.Name) ||
            HasPath(symbol, ImplementationTermCatalog.TimeSeriesPath) ||
            HasPath(ValueType(symbol), ImplementationTermCatalog.TimeSeriesPath) ||
            symbol?.ContainingType?.ToDisplayString() == ImplementationTermCatalog.SampleFieldsType ||
            symbol?.ToDisplayString() == ImplementationTermCatalog.SampleFieldsType)
        {
            return true;
        }
        if (DataType(ValueType(symbol)) || DataType(symbol?.ContainingType))
        {
            return true;
        }
        return symbol is not null && ImplementationTermCatalog.DataNamespaces.Any(prefix =>
            DataType(compilation.GetTypeByMetadataName(prefix + "." + symbol.Name)));
    }

    internal static bool Measurement(ISymbol? symbol) =>
        symbol is IFieldSymbol or IPropertySymbol && symbol.ContainingAssembly.Name == "KeyLoad.Comparisons" &&
        Array.Exists(ImplementationTermCatalog.MeasurementMembers, member => member == symbol.ToDisplayString()) ||
        symbol is IFieldSymbol { Name: "Samples", ContainingType: { } owner } &&
        owner.ToDisplayString() == "KeyLoad.SiteTests.Features.BenchmarkComparisons.SiteIsolatedFields" ||
        TypeMeans(ValueType(symbol), MeasurementType, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default)) ||
        TypeMeans(symbol?.ContainingType, MeasurementType, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default));

    private static bool MeasurementType(ITypeSymbol type) =>
        type.ContainingAssembly?.Name.StartsWith(ImplementationTermCatalog.NamespacePrefix, StringComparison.Ordinal) == true &&
        Array.Exists(ImplementationTermCatalog.MeasurementTypes, name => name == type.ToDisplayString());

    private static bool DataType(ITypeSymbol? type) => TypeMeans(type, static candidate =>
        HasPath(candidate, ImplementationTermCatalog.TimeSeriesPath) ||
        DataIdentity(candidate), new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default));

    private static bool DataIdentity(ITypeSymbol type) =>
         type.ContainingAssembly?.Name.StartsWith(ImplementationTermCatalog.NamespacePrefix, StringComparison.Ordinal) == true &&
         type.ContainingNamespace?.ToDisplayString().EndsWith(ImplementationTermCatalog.DataFeatureNamespaceSuffix, StringComparison.Ordinal) == true ||
         type.ContainingAssembly?.Name == ImplementationTermCatalog.DataAssembly &&
         type.ContainingNamespace?.ToDisplayString() == ImplementationTermCatalog.DataNamespace &&
         Array.Exists(ImplementationTermCatalog.DataContractTypes, name => name == type.Name) ||
         type.ContainingAssembly?.Name == ImplementationTermCatalog.DataRuntimeAssembly &&
         type.ContainingNamespace?.ToDisplayString() == ImplementationTermCatalog.DataRuntimeNamespace &&
         Array.Exists(ImplementationTermCatalog.DataRuntimeTypes, name => name == type.Name) ||
         type.ContainingAssembly?.Name == "KeyLoad.BenchmarkScenarios" &&
         Array.Exists(ImplementationTermCatalog.DataControlTypes, name => name == type.ToDisplayString());

    private static bool TypeMeans(ITypeSymbol? type, Func<ITypeSymbol, bool> accepts, HashSet<ITypeSymbol> visited)
    {
        if (type is null || !visited.Add(type))
        {
            return false;
        }
        if (accepts(type))
        {
            return true;
        }
        if (type is IArrayTypeSymbol array)
        {
            return TypeMeans(array.ElementType, accepts, visited);
        }
        return type is INamedTypeSymbol named &&
            (named.TypeArguments.Any(argument => TypeMeans(argument, accepts, visited)) ||
             named.AllInterfaces.Where(NativeModelContract).SelectMany(static contract => contract.TypeArguments)
                 .Any(argument => TypeMeans(argument, accepts, visited)));
    }

    private static bool NativeModelContract(INamedTypeSymbol contract) =>
        Native(contract) && Array.Exists(ImplementationTermCatalog.NativeModelContracts,
            name => name == contract.ContainingNamespace.ToDisplayString() + "." + contract.MetadataName);
}
