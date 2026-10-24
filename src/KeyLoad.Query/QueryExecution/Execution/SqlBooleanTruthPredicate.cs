namespace KeyLoad.Query.QueryExecution;

internal static class SqlBooleanTruthPredicate
{
    internal static Predicate Lower(Operand value, string target, bool negated)
    {
        var trueValue = new Comparison(value, SqlSyntax.Equals, ValueOperand.Create(true));
        Predicate result = target switch
        {
            SqlSyntax.True => And(new NullTest(value, true, false), trueValue),
            SqlSyntax.False => And(new NullTest(value, true, false),
                new Comparison(value, SqlSyntax.Equals, ValueOperand.Create(false))),
            SqlSyntax.Unknown => Or(new NullTest(value, false, false),
                And(trueValue, new Negation(trueValue))),
            _ => throw SqlSyntax.Invalid()
        };
        return negated ? new Negation(result) : result;
    }

    private static Logical And(Predicate left, Predicate right) => new(left, SqlSyntax.And, right);
    private static Logical Or(Predicate left, Predicate right) => new(left, SqlSyntax.Or, right);
}
