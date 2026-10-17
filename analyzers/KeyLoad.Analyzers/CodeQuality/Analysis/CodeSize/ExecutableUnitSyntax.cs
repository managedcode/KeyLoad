using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class ExecutableUnitSyntax
{
    internal static readonly SyntaxKind[] Kinds =
    [
        SyntaxKind.MethodDeclaration,
        SyntaxKind.ConstructorDeclaration,
        SyntaxKind.DestructorDeclaration,
        SyntaxKind.OperatorDeclaration,
        SyntaxKind.ConversionOperatorDeclaration,
        SyntaxKind.GetAccessorDeclaration,
        SyntaxKind.SetAccessorDeclaration,
        SyntaxKind.InitAccessorDeclaration,
        SyntaxKind.AddAccessorDeclaration,
        SyntaxKind.RemoveAccessorDeclaration,
        SyntaxKind.PropertyDeclaration,
        SyntaxKind.IndexerDeclaration,
        SyntaxKind.LocalFunctionStatement,
        SyntaxKind.SimpleLambdaExpression,
        SyntaxKind.ParenthesizedLambdaExpression,
        SyntaxKind.AnonymousMethodExpression
    ];

    internal static SyntaxNode? GetBody(SyntaxNode unit) => unit switch
    {
        BaseMethodDeclarationSyntax method => method.Body ?? (SyntaxNode?)method.ExpressionBody?.Expression,
        AccessorDeclarationSyntax accessor => accessor.Body ??
            (SyntaxNode?)accessor.ExpressionBody?.Expression,
        LocalFunctionStatementSyntax local => local.Body ??
            (SyntaxNode?)local.ExpressionBody?.Expression,
        PropertyDeclarationSyntax property => property.ExpressionBody?.Expression,
        IndexerDeclarationSyntax indexer => indexer.ExpressionBody?.Expression,
        SimpleLambdaExpressionSyntax simple => simple.Body,
        ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.Body,
        AnonymousMethodExpressionSyntax anonymous => anonymous.Block,
        _ => null
    };

    internal static bool IsExecutableUnit(SyntaxNode unit) => unit switch
    {
        PropertyDeclarationSyntax property => property.ExpressionBody is not null,
        IndexerDeclarationSyntax indexer => indexer.ExpressionBody is not null,
        _ => true
    };

    internal static bool IsNestedUnit(SyntaxNode node) => node.Kind() switch
    {
        SyntaxKind.MethodDeclaration or
        SyntaxKind.ConstructorDeclaration or
        SyntaxKind.DestructorDeclaration or
        SyntaxKind.OperatorDeclaration or
        SyntaxKind.ConversionOperatorDeclaration or
        SyntaxKind.GetAccessorDeclaration or
        SyntaxKind.SetAccessorDeclaration or
        SyntaxKind.InitAccessorDeclaration or
        SyntaxKind.AddAccessorDeclaration or
        SyntaxKind.RemoveAccessorDeclaration or
        SyntaxKind.PropertyDeclaration or
        SyntaxKind.IndexerDeclaration or
        SyntaxKind.LocalFunctionStatement or
        SyntaxKind.SimpleLambdaExpression or
        SyntaxKind.ParenthesizedLambdaExpression or
        SyntaxKind.AnonymousMethodExpression => true,
        _ => false
    };
}
