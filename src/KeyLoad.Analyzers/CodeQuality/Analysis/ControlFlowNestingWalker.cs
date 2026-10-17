using System;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal sealed class ControlFlowNestingWalker : CSharpSyntaxWalker
{
    private readonly SyntaxNode executableUnit;
    private readonly CancellationToken cancellationToken;
    private int currentDepth;
    private int maximumDepth;

    private ControlFlowNestingWalker(SyntaxNode executableUnit, CancellationToken cancellationToken)
    {
        this.executableUnit = executableUnit;
        this.cancellationToken = cancellationToken;
    }

    internal static int Measure(
        SyntaxNode executableUnit,
        SyntaxNode body,
        CancellationToken cancellationToken)
    {
        var walker = new ControlFlowNestingWalker(executableUnit, cancellationToken);
        walker.Visit(body);
        return walker.maximumDepth;
    }

    public override void Visit(SyntaxNode? node)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (node is null || (!ReferenceEquals(node, executableUnit) &&
            ExecutableUnitSyntax.IsNestedUnit(node)))
        {
            return;
        }

        var addsDepth = AddsDepth(node.Kind());
        if (addsDepth)
        {
            currentDepth++;
            maximumDepth = Math.Max(maximumDepth, currentDepth);
        }

        base.Visit(node);
        if (addsDepth)
        {
            currentDepth--;
        }
    }

    private static bool AddsDepth(SyntaxKind kind) => kind switch
    {
        SyntaxKind.IfStatement or
        SyntaxKind.WhileStatement or
        SyntaxKind.DoStatement or
        SyntaxKind.ForStatement or
        SyntaxKind.ForEachStatement or
        SyntaxKind.ForEachVariableStatement or
        SyntaxKind.SwitchStatement or
        SyntaxKind.SwitchExpression or
        SyntaxKind.TryStatement or
        SyntaxKind.UsingStatement or
        SyntaxKind.LockStatement or
        SyntaxKind.FixedStatement or
        SyntaxKind.ConditionalExpression => true,
        _ => false
    };
}
