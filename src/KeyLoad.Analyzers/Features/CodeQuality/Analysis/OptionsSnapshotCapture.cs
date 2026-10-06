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
        !property.IsStatic && property.SetMethod is null &&
        (HasSingleCapture(compilation, property, cancellationToken) || IsReadonlyExport(compilation, property, cancellationToken));

    private static bool IsReadonlyExport(Compilation compilation, IPropertySymbol property, CancellationToken cancellationToken) =>
        property.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax(cancellationToken) is PropertyDeclarationSyntax declaration &&
            FindGetter(declaration) is { } expression &&
            compilation.GetSemanticModel(expression.SyntaxTree).GetOperation(expression, cancellationToken) is { } operation &&
            (IsFrozenField(compilation, operation, cancellationToken) ||
             IsOptionsValue(compilation, expression, cancellationToken)));

    private static ExpressionSyntax? FindGetter(PropertyDeclarationSyntax property) =>
        property.ExpressionBody?.Expression ?? property.AccessorList?.Accessors
            .Where(static accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration))
            .Select(static accessor => accessor.ExpressionBody?.Expression ??
                (accessor.Body?.Statements is [ReturnStatementSyntax statement] ? statement.Expression : null))
            .FirstOrDefault();

    private static bool IsFrozenField(Compilation compilation, IOperation operation, CancellationToken cancellationToken) => operation switch
    {
        IFieldReferenceOperation field => IsCaptured(compilation, field.Field, cancellationToken),
        IConversionOperation conversion => IsFrozenField(compilation, conversion.Operand, cancellationToken),
        ICoalesceOperation coalesce when IsThrow(coalesce.WhenNull) => IsFrozenField(compilation, coalesce.Value, cancellationToken),
        _ => false
    };

    private static bool IsThrow(IOperation operation) => operation switch
    {
        IThrowOperation => true,
        IConversionOperation conversion => IsThrow(conversion.Operand),
        _ => false
    };

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
            return operation is IWithOperation withOperation &&
                !OptionsSnapshotOverrides.IsHardcoded(compilation, withOperation, cancellationToken) &&
                IsOptionsValue(compilation, clone.Expression, cancellationToken);
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
