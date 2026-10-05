using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class OptionsSnapshotCapture
{
    internal static bool IsCaptured(Compilation compilation, IFieldSymbol field, CancellationToken cancellationToken)
    {
        if (!field.IsReadOnly || field.DeclaredAccessibility != Accessibility.Private)
        {
            return false;
        }

        return field.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax(cancellationToken) is VariableDeclaratorSyntax { Initializer.Value: { } expression } &&
            compilation.GetSemanticModel(expression.SyntaxTree).GetOperation(expression, cancellationToken) is { } value &&
            ContainsOptionsValue(compilation, value)) ||
            field.ContainingType.DeclaringSyntaxReferences.Any(reference =>
            HasCapture(compilation, reference.GetSyntax(cancellationToken), field, cancellationToken));
    }

    private static bool HasCapture(Compilation compilation, SyntaxNode owner, IFieldSymbol field,
        CancellationToken cancellationToken)
    {
        var model = compilation.GetSemanticModel(owner.SyntaxTree);
        foreach (var assignment in owner.DescendantNodes().OfType<AssignmentExpressionSyntax>()
            .Where(static candidate => candidate.IsKind(SyntaxKind.SimpleAssignmentExpression)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(assignment.Left, cancellationToken).Symbol, field) &&
                model.GetOperation(assignment.Right, cancellationToken) is { } value && ContainsOptionsValue(compilation, value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsOptionsValue(Compilation compilation, IOperation operation) =>
        operation is IPropertyReferenceOperation property && property.Property.Name == ConfigurationMetadataNames.Value &&
            ConfigurationOwnership.IsOptionsWrapper(compilation, property.Property.ContainingType) ||
        operation.ChildOperations.Any(child => ContainsOptionsValue(compilation, child));
}
