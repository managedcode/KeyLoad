using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KeyLoad.Analyzers.CodeQuality;

internal static class ImplementationTermSymbols
{
    internal static bool Resolve(SemanticModel model, SyntaxNode? node, CancellationToken cancellationToken, out ISymbol? symbol)
    {
        symbol = null;
        if (node is null)
        {
            return false;
        }
        if (node is SimpleNameSyntax or MemberAccessExpressionSyntax)
        {
            var info = model.GetSymbolInfo(node, cancellationToken);
            if (info.Symbol is null && info.CandidateReason == CandidateReason.MemberGroup &&
                info.CandidateSymbols.Length == 1 && info.CandidateSymbols[0] is IMethodSymbol method)
            {
                symbol = method;
                return true;
            }
            symbol = info.Symbol;
            return symbol is not null;
        }
        if (node is FieldDeclarationSyntax field && field.Declaration.Variables.Count == 1)
        {
            symbol = model.GetDeclaredSymbol(field.Declaration.Variables[0], cancellationToken);
            return symbol is not null;
        }
        if (node is MemberDeclarationSyntax or VariableDeclaratorSyntax or ParameterSyntax or TypeParameterSyntax)
        {
            symbol = model.GetDeclaredSymbol(node, cancellationToken);
        }
        return symbol is not null;
    }

    internal static bool Execution(ISymbol? symbol) => symbol is not null &&
        symbol.Locations.All(static location => !location.IsInSource) &&
        ImplementationTermCatalog.RuntimeNamespace(symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty);

    internal static bool LoggingProcessor(ISymbol? symbol) => symbol is not null &&
        symbol.Locations.All(static location => !location.IsInSource) &&
        symbol.ContainingAssembly?.Name == ImplementationTermCatalog.LoggingProcessorAssembly &&
        ((symbol as INamedTypeSymbol ?? symbol.ContainingType)?.ToDisplayString() == ImplementationTermCatalog.LoggingProcessorType ||
         (symbol as INamedTypeSymbol ?? symbol.ContainingType)?.ToDisplayString() == ImplementationTermCatalog.ActivityProcessorType);

    internal static bool HasPath(ISymbol? symbol, string segment) => symbol is not null &&
        (symbol is INamedTypeSymbol { DeclaringSyntaxReferences.Length: > 0 } type
            ? type.DeclaringSyntaxReferences.All(reference => PathContains(reference.SyntaxTree.FilePath, segment))
            : symbol.DeclaringSyntaxReferences.Any(reference => PathContains(reference.SyntaxTree.FilePath, segment)) ||
              symbol.ContainingType is { DeclaringSyntaxReferences.Length: > 0 } owner &&
              owner.DeclaringSyntaxReferences.All(reference => PathContains(reference.SyntaxTree.FilePath, segment)));

    internal static bool PathContains(string path, string segment) =>
        path.Replace('\\', '/').Contains(segment, StringComparison.Ordinal);

    internal static bool ValueType(ISymbol? symbol, out ITypeSymbol? type)
    {
        type = symbol switch
        {
            ILocalSymbol local => local.Type,
            IParameterSymbol parameter => parameter.Type,
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            IMethodSymbol method => method.ReturnType,
            ITypeSymbol value => value,
            _ => null
        };
        return type is not null;
    }

    internal static bool TestOwner(ISymbol? symbol)
    {
        if (HasPath(symbol, ImplementationTermCatalog.TestPath) && HasPath(symbol, ImplementationTermCatalog.TestOwnerPath))
        {
            return true;
        }
        ValueType(symbol, out var value);
        var owner = value ?? symbol?.ContainingType;
        return TypeMeans(owner as ITypeSymbol, static type =>
            HasPath(type, ImplementationTermCatalog.TestPath) && HasPath(type, ImplementationTermCatalog.TestOwnerPath),
            new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default)) ||
            TypeMeans(symbol?.ContainingType, static type =>
                HasPath(type, ImplementationTermCatalog.TestPath) && HasPath(type, ImplementationTermCatalog.TestOwnerPath),
                new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default));
    }

    internal static bool TimeSeries(ISymbol? symbol, Compilation compilation)
    {
        ValueType(symbol, out var value);
        if (symbol is IFieldSymbol or IPropertySymbol &&
            (symbol.ContainingAssembly.Name is "KeyLoad.BenchmarkScenarios" or "KeyLoad.IntegrationTests" or "KeyLoad.UnitTests") &&
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
            HasPath(value, ImplementationTermCatalog.TimeSeriesPath) ||
            symbol?.ContainingType?.ToDisplayString() == ImplementationTermCatalog.SampleFieldsType ||
            symbol?.ToDisplayString() == ImplementationTermCatalog.SampleFieldsType)
        {
            return true;
        }
        if (DataType(value) || DataType(symbol?.ContainingType))
        {
            return true;
        }
        return symbol is not null && ImplementationTermCatalog.DataNamespaces.Any(prefix =>
            DataType(compilation.GetTypeByMetadataName(prefix + "." + symbol.Name)));
    }

    internal static bool Measurement(ISymbol? symbol)
    {
        ValueType(symbol, out var value);
        return
        symbol is IFieldSymbol or IPropertySymbol or IMethodSymbol && symbol.ContainingAssembly.Name is "KeyLoad.Comparisons" or "KeyLoad.AppHost" or "KeyLoad.LoadTests.AppHost" or "KeyLoad.ComparisonHost" &&
        Array.Exists(ImplementationTermCatalog.MeasurementMembers, member => member == symbol.ToDisplayString()) ||
        symbol is IFieldSymbol { Name: ImplementationTermCatalog.MeasurementSamplesMember, ContainingType: { } owner } &&
        owner.ToDisplayString() == "KeyLoad.SiteTests.BenchmarkComparisons.SiteIsolatedFields" ||
        TypeMeans(value, MeasurementType, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default)) ||
        TypeMeans(symbol?.ContainingType, MeasurementType, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default));
    }

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
         type.ContainingAssembly?.Name is "KeyLoad.BenchmarkScenarios" or "KeyLoad.ComparisonHost" &&
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
             named.AllInterfaces.Where(ModelContract).SelectMany(static contract => contract.TypeArguments)
                 .Any(argument => TypeMeans(argument, accepts, visited)));
    }

    private static bool ModelContract(INamedTypeSymbol contract) =>
        Execution(contract) && Array.Exists(ImplementationTermCatalog.ModelContracts,
            name => name == contract.ContainingNamespace.ToDisplayString() + "." + contract.MetadataName);
}
