using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class LiteralDeclarationOwnership
{
    internal static bool IsNamedConstant(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            if (ancestor is VariableDeclaratorSyntax { Initializer: { } initializer } variable &&
                initializer.Span.Contains(node.Span))
            {
                return variable.Parent?.Parent is FieldDeclarationSyntax field &&
                    field.Modifiers.Any(SyntaxKind.ConstKeyword) ||
                    variable.Parent?.Parent is LocalDeclarationStatementSyntax local &&
                    local.Modifiers.Any(SyntaxKind.ConstKeyword);
            }
        }

        return false;
    }

    internal static bool IsNumericMetadata(SyntaxNode node) =>
        node.Ancestors().Any(static ancestor => ancestor is AttributeArgumentSyntax or EnumMemberDeclarationSyntax);

    internal static bool IsAssemblyIdentity(SyntaxNodeAnalysisContext context) =>
        context.Node.Ancestors().OfType<AttributeSyntax>().FirstOrDefault() is { } attribute &&
        context.SemanticModel.GetSymbolInfo(attribute, context.CancellationToken).Symbol is IMethodSymbol constructor &&
        MagicRuntimeOperations.IsNativeType(context.Compilation, constructor.ContainingType, MagicRuntimeMetadataNames.FriendAssemblyAttribute);

    internal static bool IsOptionsDefault(SyntaxNodeAnalysisContext context)
    {
        return IsOptionsDefault(context.Compilation, context.Node,
            context.SemanticModel.GetEnclosingSymbol(context.Node.SpanStart, context.CancellationToken));
    }

    internal static bool IsOptionsDefault(Compilation compilation, SyntaxNode node, ISymbol? containingSymbol)
    {
        var owner = node.Ancestors().FirstOrDefault(static node =>
            node is FieldDeclarationSyntax or PropertyDeclarationSyntax or BaseMethodDeclarationSyntax);
        var isDefinition = owner is ConstructorDeclarationSyntax && IsConstructorDefault(compilation, node, containingSymbol) ||
            owner is FieldDeclarationSyntax field && field.Declaration.Variables.Any(variable =>
                variable.Initializer?.Span.Contains(node.Span) == true) ||
            owner is PropertyDeclarationSyntax property && property.Initializer?.Span.Contains(node.Span) == true;
        return isDefinition && ConfigurationOwnership.IsWithinOptions(compilation, containingSymbol);
    }

    private static bool IsConstructorDefault(Compilation compilation, SyntaxNode node, ISymbol? containingSymbol)
    {
        var assignment = node.Ancestors().OfType<AssignmentExpressionSyntax>().FirstOrDefault();
        if (assignment is null || !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
            !assignment.Right.Span.Contains(node.Span))
        {
            return false;
        }

        var target = compilation.GetSemanticModel(node.SyntaxTree).GetSymbolInfo(assignment.Left).Symbol;
        return target is IFieldSymbol or IPropertySymbol &&
            SymbolEqualityComparer.Default.Equals(target.ContainingType, containingSymbol?.ContainingType);
    }
}
