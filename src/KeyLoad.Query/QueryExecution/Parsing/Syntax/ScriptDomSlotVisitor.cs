using KeyLoad.Core;
using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.QueryExecution;

internal sealed class ScriptDomSlotVisitor(int? positionalCount,
    Action checkBudget, CancellationToken cancellationToken) : Dom.TSqlFragmentVisitor
{
    public override void Visit(Dom.TSqlFragment fragment)
    {
        cancellationToken.ThrowIfCancellationRequested();
        checkBudget();
        ExecutionStackGuard.Ensure();
    }
    public override void ExplicitVisit(Dom.MoneyLiteral literal)
    {
        Visit((Dom.TSqlFragment)literal);
        if (positionalCount is { } count && !ScriptDomSlots.TryGet(literal, count, out _))
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
        ExecutionStackGuard.Ensure();
        visit();
    }
}
