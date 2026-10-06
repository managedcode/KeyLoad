using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class HardcodedPolicyReturns
{
    internal static IEnumerable<IOperation> Read(Compilation compilation, ISymbol symbol, CancellationToken cancellationToken)
    {
        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var declaration = reference.GetSyntax(cancellationToken);
            var expression = declaration switch
            {
                MethodDeclarationSyntax method => method.ExpressionBody?.Expression,
                LocalFunctionStatementSyntax local => local.ExpressionBody?.Expression,
                PropertyDeclarationSyntax property => property.ExpressionBody?.Expression,
                AccessorDeclarationSyntax accessor => accessor.ExpressionBody?.Expression,
                _ => null
            };
            var model = compilation.GetSemanticModel(declaration.SyntaxTree);
            if (expression is not null && model.GetOperation(expression, cancellationToken) is { } operation)
            {
                yield return operation;
            }
            foreach (var statement in declaration.DescendantNodes(descendIntoChildren: node =>
                         node == declaration || node is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax)
                         .OfType<ReturnStatementSyntax>())
            {
                if (statement.Expression is { } value && model.GetOperation(value, cancellationToken) is { } returned)
                {
                    yield return returned;
                }
            }
        }
    }
}
