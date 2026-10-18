using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class ImplementationTestOwnership
{
    internal static HashSet<INamedTypeSymbol> Read(Compilation compilation, CancellationToken token)
    {
        var owners = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var pending = new Queue<INamedTypeSymbol>();
        foreach (var tree in compilation.SyntaxTrees.Where(tree =>
            ImplementationTermSymbols.PathContains(tree.FilePath, ImplementationTermCatalog.TestPath)))
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var declaration in tree.GetRoot(token).DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                token.ThrowIfCancellationRequested();
                if (model.GetDeclaredSymbol(declaration, token) is INamedTypeSymbol type &&
                    (ImplementationTermSymbols.HasPath(type, ImplementationTermCatalog.TestOwnerPath) ||
                     type.GetMembers().Any(NativeTest)))
                {
                    pending.Enqueue(type);
                }
            }
        }
        while (pending.TryDequeue(out var owner))
        {
            token.ThrowIfCancellationRequested();
            if (!owners.Add(owner.OriginalDefinition))
            {
                continue;
            }
            AddReferences(compilation, owner, pending, token);
        }
        return owners;
    }

    private static bool NativeTest(ISymbol member) => member.GetAttributes().Any(static attribute =>
        attribute.AttributeClass is { Name: "TestAttribute" } type &&
        ImplementationTermSymbols.Native(type) &&
        type.ContainingNamespace.ToDisplayString().StartsWith("TUnit.", StringComparison.Ordinal));

    private static void AddReferences(Compilation compilation, INamedTypeSymbol owner,
        Queue<INamedTypeSymbol> pending, CancellationToken token)
    {
        foreach (var declaration in owner.DeclaringSyntaxReferences)
        {
            var model = compilation.GetSemanticModel(declaration.SyntaxTree);
            foreach (var name in declaration.GetSyntax(token).DescendantNodes().OfType<SimpleNameSyntax>())
            {
                token.ThrowIfCancellationRequested();
                var symbol = model.GetSymbolInfo(name, token).Symbol;
                var type = symbol as INamedTypeSymbol ?? symbol?.ContainingType;
                if (type is not null && ImplementationTermSymbols.HasPath(type, ImplementationTermCatalog.TestPath))
                {
                    pending.Enqueue(type.OriginalDefinition);
                }
            }
        }
    }
}
