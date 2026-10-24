using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KeyLoad.Analyzers.CodeQuality;

internal static class ImplementationTestOwnership
{
    internal static HashSet<INamedTypeSymbol> Read(Compilation compilation, CancellationToken token)
    {
        var owners = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var pending = new Queue<INamedTypeSymbol>();
        var runtimeAspire = AspirePath(compilation) is not null && AspireEntry(compilation, token);
        AddProcessEntry(compilation, pending, token);
        foreach (var tree in compilation.SyntaxTrees.Where(tree => ExecutionPath(tree.FilePath)))
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var declaration in tree.GetRoot(token).DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                token.ThrowIfCancellationRequested();
                if (model.GetDeclaredSymbol(declaration, token) is INamedTypeSymbol type &&
                    (ImplementationTermSymbols.HasPath(type, ImplementationTermCatalog.TestPath) &&
                     ImplementationTermSymbols.HasPath(type, ImplementationTermCatalog.TestOwnerPath) || RuntimeExecution(type) ||
                     runtimeAspire && AspireCallback(type)))
                {
                    pending.Enqueue(type);
                }
            }
        }
        while (pending.Count != 0)
        {
            var owner = pending.Dequeue();
            token.ThrowIfCancellationRequested();
            if (!owners.Add(owner.OriginalDefinition))
            {
                continue;
            }
            AddReferences(compilation, owner, pending, token);
        }
        return owners;
    }

    private static bool ExecutionPath(string path) =>
        ImplementationTermSymbols.PathContains(path, ImplementationTermCatalog.TestPath) ||
        ImplementationTermSymbols.PathContains(path, ImplementationTermCatalog.BenchmarkPath) ||
        ImplementationTermSymbols.PathContains(path, ImplementationTermCatalog.AspireHostPath) ||
        ImplementationTermSymbols.PathContains(path, ImplementationTermCatalog.LoadTestsHostPath);

    private static bool RuntimeExecution(INamedTypeSymbol type) => type.GetMembers().Any(member =>
        member.GetAttributes().Any(static attribute => ExecutionAttribute(attribute.AttributeClass))) ||
        type.GetAttributes().Any(static attribute => attribute.AttributeClass is { Name: "ClassDataSourceAttribute", Arity: 1 } source &&
            ImplementationTermSymbols.Execution(source) && source.ContainingNamespace.ToDisplayString() == "TUnit.Core") ||
        type.AllInterfaces.Any(static contract => ImplementationTermSymbols.Execution(contract) &&
            contract.ToDisplayString() == "TUnit.Core.Interfaces.IAsyncInitializer");

    private static bool ExecutionAttribute(INamedTypeSymbol? type) =>
        type is not null && ImplementationTermSymbols.Execution(type) &&
        (type.Name == "TestAttribute" && type.ContainingNamespace.ToDisplayString().StartsWith("TUnit.", StringComparison.Ordinal) ||
         type.Name is "BenchmarkAttribute" or "GlobalSetupAttribute" or "GlobalCleanupAttribute" &&
         type.ContainingNamespace.ToDisplayString() == "BenchmarkDotNet.Attributes");

    private static bool AspireCallback(INamedTypeSymbol type) =>
        (ImplementationTermSymbols.HasPath(type, ImplementationTermCatalog.AspireHostPath) ||
         ImplementationTermSymbols.HasPath(type, ImplementationTermCatalog.LoadTestsHostPath)) &&
        type.GetMembers().OfType<IMethodSymbol>().Any(static method => AspireBuilder(method.ReturnType) ||
            method.Parameters.Any(static parameter => AspireBuilder(parameter.Type)));

    private static bool AspireBuilder(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol parameter => parameter.ConstraintTypes.Any(AspireBuilder),
        INamedTypeSymbol named => AspireBuilderContract(named) || named.AllInterfaces.Any(AspireBuilderContract),
        _ => false
    };

    private static bool AspireBuilderContract(INamedTypeSymbol type) =>
        ImplementationTermSymbols.Execution(type) && type.ToDisplayString() == "Aspire.Hosting.IDistributedApplicationBuilder";

    private static void AddProcessEntry(Compilation compilation, Queue<INamedTypeSymbol> pending, CancellationToken token)
    {
        var path = compilation.AssemblyName switch
        {
            "KeyLoad.RecoveryTests" => ImplementationTermCatalog.TestPath,
            "KeyLoad.AppHost" when AspireEntry(compilation, token) => ImplementationTermCatalog.AspireHostPath,
            "KeyLoad.LoadTests.AppHost" when AspireEntry(compilation, token) => ImplementationTermCatalog.LoadTestsHostPath,
            _ => null
        };
        if (path is null || compilation.GetEntryPoint(token) is not { } entry)
        {
            return;
        }
        foreach (var reference in entry.DeclaringSyntaxReferences.Where(reference =>
            ImplementationTermSymbols.PathContains(reference.SyntaxTree.FilePath, path)))
        {
            AddReferences(compilation, reference.GetSyntax(token), pending, token);
        }
        foreach (var tree in compilation.SyntaxTrees.Where(tree =>
            ImplementationTermSymbols.PathContains(tree.FilePath, path)))
        {
            if (tree.GetRoot(token) is CompilationUnitSyntax root)
            {
                foreach (var statement in root.Members.OfType<GlobalStatementSyntax>())
                {
                    AddReferences(compilation, statement, pending, token);
                }
            }
        }
    }

    private static string? AspirePath(Compilation compilation) => compilation.AssemblyName switch
    {
        "KeyLoad.AppHost" => ImplementationTermCatalog.AspireHostPath,
        "KeyLoad.LoadTests.AppHost" => ImplementationTermCatalog.LoadTestsHostPath,
        _ => null
    };

    private static bool AspireEntry(Compilation compilation, CancellationToken token)
    {
        var path = AspirePath(compilation);
        if (path is null)
        {
            return false;
        }
        foreach (var tree in compilation.SyntaxTrees.Where(tree =>
            ImplementationTermSymbols.PathContains(tree.FilePath, path)))
        {
            var model = compilation.GetSemanticModel(tree);
            var calls = tree.GetRoot(token).ChildNodes().OfType<GlobalStatementSyntax>()
                .SelectMany(static statement => statement.DescendantNodes().OfType<InvocationExpressionSyntax>());
            foreach (var call in calls)
            {
                token.ThrowIfCancellationRequested();
                if (model.GetSymbolInfo(call, token).Symbol is IMethodSymbol { Name: "CreateBuilder" } method &&
                    method.ContainingType.ToDisplayString() == "Aspire.Hosting.DistributedApplication" &&
                    ImplementationTermSymbols.Execution(method))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static void AddReferences(Compilation compilation, INamedTypeSymbol owner,
        Queue<INamedTypeSymbol> pending, CancellationToken token)
    {
        foreach (var declaration in owner.DeclaringSyntaxReferences)
        {
            AddReferences(compilation, declaration.GetSyntax(token), pending, token);
        }
    }

    private static void AddReferences(Compilation compilation, SyntaxNode source,
        Queue<INamedTypeSymbol> pending, CancellationToken token)
    {
        var model = compilation.GetSemanticModel(source.SyntaxTree);
        foreach (var name in source.DescendantNodesAndSelf().OfType<SimpleNameSyntax>())
        {
            token.ThrowIfCancellationRequested();
            var symbol = model.GetSymbolInfo(name, token).Symbol;
            var type = symbol as INamedTypeSymbol ?? symbol?.ContainingType;
            if (type is not null && type.DeclaringSyntaxReferences.Any(reference => ExecutionPath(reference.SyntaxTree.FilePath)))
            {
                pending.Enqueue(type.OriginalDefinition);
            }
        }
    }
}
