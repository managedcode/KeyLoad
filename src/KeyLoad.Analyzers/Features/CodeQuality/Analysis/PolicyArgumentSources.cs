using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal sealed class PolicyArgumentSources
{
    private static readonly ConditionalWeakTable<Compilation, PolicyArgumentSources> ByCompilation = new();
    private Lazy<Dictionary<ISymbol, List<IOperation>>>? sources;
    private readonly Compilation compilation;

    private PolicyArgumentSources(Compilation compilation) => this.compilation = compilation;

    private static Dictionary<ISymbol, List<IOperation>> ReadSources(Compilation compilation, CancellationToken cancellationToken)
    {
        var result = new Dictionary<ISymbol, List<IOperation>>(SymbolEqualityComparer.Default);
        foreach (var tree in compilation.SyntaxTrees)
        {
            IndexTree(compilation, tree, result, cancellationToken);
        }
        return result;
    }

    private static void IndexTree(
        Compilation compilation,
        SyntaxTree tree,
        Dictionary<ISymbol, List<IOperation>> result,
        CancellationToken cancellationToken)
    {
        var model = compilation.GetSemanticModel(tree);
        foreach (var node in tree.GetRoot(cancellationToken).DescendantNodes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsArgumentSource(node))
            {
                AddArguments(model.GetOperation(node, cancellationToken), result);
            }
        }
    }

    private static bool IsArgumentSource(SyntaxNode node) =>
        node is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax or ConstructorInitializerSyntax;

    private static void AddArguments(IOperation? operation, Dictionary<ISymbol, List<IOperation>> result)
    {
        var arguments = operation switch
        {
            IInvocationOperation invocation => invocation.Arguments,
            IObjectCreationOperation creation => creation.Arguments,
            _ => ImmutableArray<IArgumentOperation>.Empty
        };
        foreach (var argument in arguments)
        {
            AddArgument(argument, result);
        }
    }

    private static void AddArgument(IArgumentOperation argument, Dictionary<ISymbol, List<IOperation>> result)
    {
        if (argument.Parameter is not { } parameter || parameter.DeclaringSyntaxReferences.IsEmpty)
        {
            return;
        }
        if (!result.TryGetValue(parameter.OriginalDefinition, out var values))
        {
            values = [];
            result.Add(parameter.OriginalDefinition, values);
        }
        values.Add(argument.Value);
    }

    internal static IEnumerable<IOperation> Read(Compilation compilation, IParameterSymbol parameter, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var index = ByCompilation.GetValue(compilation, static current => new PolicyArgumentSources(current));
        return index.GetSources(cancellationToken).TryGetValue(parameter.OriginalDefinition, out var values) ? values : [];
    }

    private Dictionary<ISymbol, List<IOperation>> GetSources(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = Volatile.Read(ref sources);
        if (current is null)
        {
            var candidate = new Lazy<Dictionary<ISymbol, List<IOperation>>>(() => ReadSources(compilation, cancellationToken));
            current = Interlocked.CompareExchange(ref sources, candidate, null) ?? candidate;
        }
        Dictionary<ISymbol, List<IOperation>> result;
        try
        {
            result = current.Value;
        }
        catch (Exception)
        {
            // A failed initialization cannot retain its cancellation token for later analyses.
            Interlocked.CompareExchange(ref sources, null, current);
            throw;
        }
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }
}
