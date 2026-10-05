using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class OptionsSnapshotCapture
{
    internal static bool IsCaptured(Compilation compilation, IFieldSymbol field, CancellationToken cancellationToken) =>
        field.IsReadOnly && field.DeclaredAccessibility == Accessibility.Private && !field.IsStatic &&
        HasSingleCapture(compilation, field, cancellationToken);

    internal static bool IsCaptured(Compilation compilation, IPropertySymbol property, CancellationToken cancellationToken) =>
        !property.IsStatic && property.SetMethod is null && HasSingleCapture(compilation, property, cancellationToken);

    private static bool HasSingleCapture(Compilation compilation, ISymbol symbol, CancellationToken cancellationToken)
    {
        var values = symbol.DeclaringSyntaxReferences.Select(reference => reference.GetSyntax(cancellationToken))
            .OfType<VariableDeclaratorSyntax>().Select(variable => variable.Initializer?.Value)
            .OfType<ExpressionSyntax>().ToList();
        foreach (var reference in symbol.ContainingType.DeclaringSyntaxReferences)
        {
            var owner = reference.GetSyntax(cancellationToken);
            var model = compilation.GetSemanticModel(owner.SyntaxTree);
            foreach (var assignment in owner.DescendantNodes().OfType<AssignmentExpressionSyntax>().Where(candidate =>
                candidate.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(candidate.Left, cancellationToken).Symbol, symbol)))
            {
                if (!assignment.Ancestors().Any(static ancestor => ancestor is ConstructorDeclarationSyntax))
                {
                    return false;
                }
                values.Add(assignment.Right);
            }
        }
        return values.Count == ConfigurationMetadataNames.SingleSnapshotAssignment &&
            IsOptionsValue(compilation, values[0], cancellationToken);
    }

    private static bool IsOptionsValue(Compilation compilation, ExpressionSyntax expression, CancellationToken cancellationToken)
    {
        var model = compilation.GetSemanticModel(expression.SyntaxTree);
        var operation = model.GetOperation(expression, cancellationToken);
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }
        if (operation is IPropertyReferenceOperation property)
        {
            return property.Property.Name == ConfigurationMetadataNames.Value &&
                ConfigurationOwnership.IsOptionsWrapper(compilation, property.Property.ContainingType);
        }
        if (expression is WithExpressionSyntax clone)
        {
            return IsOptionsValue(compilation, clone.Expression, cancellationToken);
        }
        if (operation is ILocalReferenceOperation local &&
            local.Local.DeclaringSyntaxReferences.SingleOrDefault()?.GetSyntax(cancellationToken) is
                VariableDeclaratorSyntax { Initializer.Value: { } initializer } variable &&
            initializer.Span.End < expression.SpanStart &&
            variable.Ancestors().FirstOrDefault(static ancestor => ancestor is BaseMethodDeclarationSyntax) is { } owner)
        {
            var reassigned = owner.DescendantNodes().OfType<AssignmentExpressionSyntax>().Any(assignment =>
                SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(assignment.Left, cancellationToken).Symbol, local.Local));
            var passedByReference = owner.DescendantNodes().OfType<ArgumentSyntax>().Any(argument =>
                !argument.RefKindKeyword.IsKind(SyntaxKind.None) &&
                SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(argument.Expression, cancellationToken).Symbol, local.Local));
            return !reassigned && !passedByReference && IsOptionsValue(compilation, initializer, cancellationToken);
        }
        return false;
    }
}
