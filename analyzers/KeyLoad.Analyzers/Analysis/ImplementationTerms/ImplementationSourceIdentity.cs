using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class ImplementationSourceIdentity
{
    internal static bool Allows(string term, SyntaxNode node, SemanticModel model,
        Lazy<HashSet<INamedTypeSymbol>> testOwners, CancellationToken token)
    {
        var attribute = node.AncestorsAndSelf().OfType<AttributeSyntax>().FirstOrDefault();
        if (attribute?.ArgumentList is not { Arguments.Count: 2 } arguments ||
            model.GetSymbolInfo(attribute, token).Symbol is not IMethodSymbol constructor ||
            constructor.ContainingType.ToDisplayString() != "System.Reflection.AssemblyMetadataAttribute" ||
            !ImplementationTermSymbols.Execution(constructor) ||
            model.GetConstantValue(arguments.Arguments[0].Expression, token).Value is not
                "KeyLoad.FunctionalCompileIdentity.Source" ||
            model.GetConstantValue(arguments.Arguments[1].Expression, token).Value is not string identity)
        {
            return false;
        }
        var separator = identity.LastIndexOf('|');
        if (separator <= 0 || identity.Length - separator - 1 != 64 ||
            !identity.Substring(separator + 1).All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'))
        {
            return false;
        }
        var path = identity.Substring(0, separator).Replace('\\', '/');
        var source = model.Compilation.SyntaxTrees.FirstOrDefault(tree =>
            tree.FilePath.Replace('\\', '/').EndsWith("/" + path, StringComparison.Ordinal));
        if (source is null)
        {
            return false;
        }
        return term == ImplementationTermCatalog.ResourceWord &&
            (ImplementationTermSymbols.PathContains(source.FilePath, ImplementationTermCatalog.TestPath) &&
             ImplementationTermSymbols.PathContains(source.FilePath, ImplementationTermCatalog.TestOwnerPath) ||
             HasExecutionOwner(source, model.Compilation, testOwners, token)) ||
            term == ImplementationTermCatalog.DataWord &&
            (ImplementationTermSymbols.PathContains(source.FilePath, ImplementationTermCatalog.TimeSeriesPath) ||
             HasDomainRecords(source, model.Compilation, token));
    }

    private static bool HasExecutionOwner(SyntaxTree source, Compilation compilation,
        Lazy<HashSet<INamedTypeSymbol>> owners, CancellationToken token)
    {
        var model = compilation.GetSemanticModel(source);
        return source.GetRoot(token).DescendantNodes().OfType<TypeDeclarationSyntax>().Any(declaration =>
            model.GetDeclaredSymbol(declaration, token) is INamedTypeSymbol type && owners.Value.Contains(type.OriginalDefinition));
    }

    private static bool HasDomainRecords(SyntaxTree source, Compilation compilation, CancellationToken token)
    {
        var model = compilation.GetSemanticModel(source);
        return source.GetRoot(token).DescendantNodes().OfType<TypeDeclarationSyntax>().Any(declaration =>
            ImplementationTermSymbols.Measurement(model.GetDeclaredSymbol(declaration, token)) ||
            ImplementationTermSymbols.TimeSeries(model.GetDeclaredSymbol(declaration, token), compilation));
    }
}
