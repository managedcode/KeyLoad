using System;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class ImplementationSourceIdentity
{
    internal static bool Allows(string term, SyntaxNode node, SemanticModel model, CancellationToken token)
    {
        var attribute = node.AncestorsAndSelf().OfType<AttributeSyntax>().FirstOrDefault();
        if (attribute?.ArgumentList is not { Arguments.Count: 2 } arguments ||
            model.GetSymbolInfo(attribute, token).Symbol is not IMethodSymbol constructor ||
            constructor.ContainingType.ToDisplayString() != "System.Reflection.AssemblyMetadataAttribute" ||
            !ImplementationTermSymbols.Native(constructor) ||
            model.GetConstantValue(arguments.Arguments[0].Expression, token).Value is not
                "KeyLoad.FunctionalCompileIdentity.Source" ||
            model.GetConstantValue(arguments.Arguments[1].Expression, token).Value is not string identity)
        {
            return false;
        }
        var separator = identity.LastIndexOf('|');
        if (separator <= 0 || identity.Length - separator - 1 != 64 ||
            !identity.AsSpan(separator + 1).ToArray().All(char.IsAsciiHexDigitLower))
        {
            return false;
        }
        var path = identity[..separator].Replace('\\', '/');
        var source = model.Compilation.SyntaxTrees.FirstOrDefault(tree =>
            tree.FilePath.Replace('\\', '/').EndsWith("/" + path, StringComparison.Ordinal));
        if (source is null)
        {
            return false;
        }
        return term == ImplementationTermCatalog.DataWord &&
            (ImplementationTermSymbols.PathContains(source.FilePath, ImplementationTermCatalog.TimeSeriesPath) ||
             HasDomainRecords(source, model.Compilation, token));
    }

    private static bool HasDomainRecords(SyntaxTree source, Compilation compilation, CancellationToken token)
    {
        var model = compilation.GetSemanticModel(source);
        return source.GetRoot(token).DescendantNodes().OfType<TypeDeclarationSyntax>().Any(declaration =>
            ImplementationTermSymbols.Measurement(model.GetDeclaredSymbol(declaration, token)) ||
            ImplementationTermSymbols.TimeSeries(model.GetDeclaredSymbol(declaration, token), compilation));
    }
}
