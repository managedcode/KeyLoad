namespace KeyLoad.Query.Features.QueryExecution;

internal static class SqlBetweenPredicate
{
    internal static Predicate Lower(Operand value, Operand lower, Operand upper, bool negated, bool symmetric)
    {
        Predicate range = Inclusive(value, lower, upper);
        if (symmetric)
        {
            range = new Logical(range, SqlSyntax.Or, Inclusive(value, upper, lower));
        }
        return negated ? new Negation(range) : range;
    }

    private static Logical Inclusive(Operand value, Operand lower, Operand upper) => new(
        new Comparison(value, SqlSyntax.GreaterOrEqual, lower), SqlSyntax.And,
        new Comparison(value, SqlSyntax.LessOrEqual, upper));
}
