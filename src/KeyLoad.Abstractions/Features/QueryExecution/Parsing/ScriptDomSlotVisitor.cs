using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.Features.QueryExecution;

internal sealed class ScriptDomSlotVisitor(ScriptDomCheckedTokens bounds, int? positionalCount,
    Action checkBudget, CancellationToken cancellationToken) : Dom.TSqlFragmentVisitor
{
    private int nodes;
    private int depth;
    public override void Visit(Dom.TSqlFragment fragment)
    {
        cancellationToken.ThrowIfCancellationRequested();
        checkBudget();
        if (++nodes > bounds.MaximumNodes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ScriptDomErrors.NodesDetail);
        }
    }
    public override void ExplicitVisit(Dom.MoneyLiteral literal)
    {
        Visit((Dom.TSqlFragment)literal);
        if (positionalCount is { } count && !ScriptDomNativeSlots.TryGet(literal, count, out _))
        {
            throw ScriptDomErrors.Invalid();
        }
    }
    public override void ExplicitVisit(Dom.VariableReference variable)
    {
        Visit((Dom.TSqlFragment)variable);
        if (positionalCount.HasValue)
        {
            throw ScriptDomErrors.Invalid();
        }
    }
    public override void ExplicitVisit(Dom.BooleanBinaryExpression fragment) => Nested(() => base.ExplicitVisit(fragment));
    public override void ExplicitVisit(Dom.BooleanNotExpression fragment) => Nested(() => base.ExplicitVisit(fragment));
    public override void ExplicitVisit(Dom.BooleanParenthesisExpression fragment) => Nested(() => base.ExplicitVisit(fragment));
    public override void ExplicitVisit(Dom.ParenthesisExpression fragment) => Nested(() => base.ExplicitVisit(fragment));
    public override void ExplicitVisit(Dom.UnaryExpression fragment) => Nested(() => base.ExplicitVisit(fragment));
    public override void ExplicitVisit(Dom.QualifiedJoin fragment) => Nested(() => base.ExplicitVisit(fragment));
    public override void ExplicitVisit(Dom.BinaryQueryExpression fragment) => Nested(() => base.ExplicitVisit(fragment));
    public override void ExplicitVisit(Dom.QueryParenthesisExpression fragment) => Nested(() => base.ExplicitVisit(fragment));
    private void Nested(Action visit)
    {
        cancellationToken.ThrowIfCancellationRequested();
        checkBudget();
        if (++depth > bounds.MaximumDepth)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ScriptDomErrors.DepthDetail);
        }
        try
        {
            visit();
        }
        finally { depth--; }
    }
}
