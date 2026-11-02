using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KeyLoad.Analyzers.CodeQuality;

internal static class UntypedSiteReason
{
    internal static bool Present(SyntaxNode node, string diagnosticId)
    {
        if (EndsInsideAnotherExpression(node))
        {
            return false;
        }
        var prefix = UntypedContractPolicy.CommentStart + diagnosticId + UntypedContractPolicy.ReasonSeparator;
        foreach (var trivia in node.GetTrailingTrivia())
        {
            if (!trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
            {
                continue;
            }
            var text = trivia.ToString();
            if (text.StartsWith(prefix, StringComparison.Ordinal) && text.EndsWith(UntypedContractPolicy.CommentEnd,
                StringComparison.Ordinal) && Meaningful(text.Substring(prefix.Length,
                    text.Length - prefix.Length - UntypedContractPolicy.CommentEnd.Length)))
            {
                return true;
            }
        }
        return false;
    }

    private static bool EndsInsideAnotherExpression(SyntaxNode node)
    {
        for (var parent = node.Parent; parent is not null && parent.Span.End == node.Span.End; parent = parent.Parent)
        {
            if (parent is ExpressionSyntax)
            {
                return true;
            }
        }
        return false;
    }

    private static bool Meaningful(string reason)
    {
        var characters = 0;
        var hasLetter = false;
        foreach (var character in reason)
        {
            if (!char.IsWhiteSpace(character))
            {
                characters++;
            }
            hasLetter |= char.IsLetter(character);
        }
        return hasLetter && characters >= UntypedContractPolicy.MinimumReasonCharacters;
    }
}
