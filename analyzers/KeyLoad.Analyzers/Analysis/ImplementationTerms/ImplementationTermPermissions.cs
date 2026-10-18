using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal sealed class ImplementationTermPermissions(SemanticModel model)
{
    private readonly Dictionary<(MemberDeclarationSyntax Owner, string Term), bool> references = new();

    internal bool Allows(string term, string text, SyntaxNode node, CancellationToken cancellationToken)
    {
        var symbol = ImplementationTermSymbols.Resolve(model, node, cancellationToken);
        if (ImplementationTermSymbols.Native(symbol) || IsVocabulary(symbol))
        {
            return true;
        }
        var owner = node.AncestorsAndSelf().OfType<MemberDeclarationSyntax>().FirstOrDefault();
        var ownerSymbol = ImplementationTermSymbols.Resolve(model, owner, cancellationToken);
        if (IsVocabulary(ownerSymbol))
        {
            return true;
        }
        return term switch
        {
            ImplementationTermCatalog.MovementWord => NativeMovement(text),
            ImplementationTermCatalog.DataWord => ImplementationTermSymbols.TimeSeries(symbol, model.Compilation) ||
                ImplementationTermSymbols.TimeSeries(ownerSymbol, model.Compilation) ||
                References(owner, term, static (candidate, compilation) => ImplementationTermSymbols.TimeSeries(candidate, compilation), cancellationToken),
            ImplementationTermCatalog.ResourceWord => ImplementationTermSymbols.TestOwner(symbol) ||
                ImplementationTermSymbols.TestOwner(ownerSymbol) ||
                References(owner, term, static (candidate, _) => ImplementationTermSymbols.TestOwner(candidate), cancellationToken),
            _ => false
        };
    }

    private static bool IsVocabulary(ISymbol? symbol) =>
        symbol?.ContainingType?.ToDisplayString() == ImplementationTermCatalog.RuleVocabularyType &&
        (symbol is IFieldSymbol { IsConst: true } ||
         symbol is IFieldSymbol { IsStatic: true, Name: nameof(ImplementationTermCatalog.DataContractTypes) } ||
         symbol is IMethodSymbol { Name: nameof(ImplementationTermCatalog.Read) or "Classify" or "Compound" });

    private static bool NativeMovement(string text) =>
        text.Contains(ImplementationTermCatalog.OrleansWord, StringComparison.Ordinal) &&
        text.Contains(ImplementationTermCatalog.ActivationWord, StringComparison.OrdinalIgnoreCase);

    private bool References(MemberDeclarationSyntax? owner, string term,
        Func<ISymbol?, Compilation, bool> accepts, CancellationToken cancellationToken)
    {
        if (owner is null)
        {
            return false;
        }
        var key = (owner, term);
        if (references.TryGetValue(key, out var allowed))
        {
            return allowed;
        }
        foreach (var name in owner.DescendantNodes().OfType<SimpleNameSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (accepts(ImplementationTermSymbols.Resolve(model, name, cancellationToken), model.Compilation))
            {
                references.Add(key, true);
                return true;
            }
        }
        references.Add(key, false);
        return false;
    }
}
