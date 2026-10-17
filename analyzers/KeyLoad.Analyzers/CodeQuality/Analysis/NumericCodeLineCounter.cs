using System;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class NumericCodeLineCounter
{
    internal static int Count(SyntaxNode root, TextSpan scope, CancellationToken cancellationToken)
    {
        var text = root.SyntaxTree.GetText(cancellationToken);
        var firstLine = text.Lines.GetLineFromPosition(scope.Start).LineNumber;
        var lastPosition = Math.Max(scope.Start, scope.End - 1);
        var lastLine = text.Lines.GetLineFromPosition(lastPosition).LineNumber;
        var counted = new bool[lastLine - firstLine + 1];
        MarkTokenLines(root, scope, text.Lines, counted, firstLine, cancellationToken);
        MarkDirectiveLines(root, scope, text, counted, firstLine, cancellationToken);
        return counted.Count(static line => line);
    }

    private static void MarkTokenLines(
        SyntaxNode root,
        TextSpan scope,
        TextLineCollection lines,
        bool[] counted,
        int scopeFirstLine,
        CancellationToken cancellationToken)
    {
        foreach (var token in root.DescendantTokens(descendIntoTrivia: false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (token.IsMissing || token.Span.Length == 0 || !scope.IntersectsWith(token.Span))
            {
                continue;
            }

            var start = Math.Max(scope.Start, token.SpanStart);
            var end = Math.Min(scope.End, token.Span.End) - 1;
            MarkRange(start, end, lines, counted, scopeFirstLine, cancellationToken);
        }
    }

    private static void MarkDirectiveLines(
        SyntaxNode root,
        TextSpan scope,
        SourceText text,
        bool[] counted,
        int scopeFirstLine,
        CancellationToken cancellationToken)
    {
        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((!trivia.IsDirective && !trivia.IsKind(SyntaxKind.DisabledTextTrivia)) ||
                trivia.Span.Length == 0 || !scope.IntersectsWith(trivia.Span))
            {
                continue;
            }

            var start = Math.Max(scope.Start, trivia.SpanStart);
            var end = Math.Min(scope.End, trivia.Span.End) - 1;
            var rangeFirstLine = text.Lines.GetLineFromPosition(start).LineNumber;
            var rangeLastLine = text.Lines.GetLineFromPosition(end).LineNumber;
            for (var lineIndex = rangeFirstLine; lineIndex <= rangeLastLine; lineIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = text.Lines[lineIndex];
                var segmentStart = Math.Max(start, line.Start);
                var segmentEnd = Math.Min(end + 1, line.End);
                if (segmentEnd > segmentStart &&
                    !string.IsNullOrWhiteSpace(text.ToString(TextSpan.FromBounds(segmentStart, segmentEnd))))
                {
                    counted[lineIndex - scopeFirstLine] = true;
                }
            }
        }
    }

    private static void MarkRange(
        int start,
        int end,
        TextLineCollection lines,
        bool[] counted,
        int scopeFirstLine,
        CancellationToken cancellationToken)
    {
        if (end < start)
        {
            return;
        }

        var rangeFirstLine = lines.GetLineFromPosition(start).LineNumber;
        var rangeLastLine = lines.GetLineFromPosition(end).LineNumber;
        for (var lineIndex = rangeFirstLine; lineIndex <= rangeLastLine; lineIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            counted[lineIndex - scopeFirstLine] = true;
        }
    }
}
