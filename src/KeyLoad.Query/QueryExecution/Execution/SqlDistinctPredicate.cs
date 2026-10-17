namespace KeyLoad.Query.Features.QueryExecution;

internal static class SqlDistinctPredicate
{
    internal static Predicate Lower(Operand left, Operand right, bool negated)
    {
        var absent = Or(new NullTest(left, false, true), new NullTest(right, false, true));
        var incomparable = And(absent, new Comparison(left, SqlSyntax.Equals, right));
        var oneNull = Or(And(new NullTest(left, false, false), new NullTest(right, true, false)),
            And(new NullTest(left, true, false), new NullTest(right, false, false)));
        var neitherNull = And(new NullTest(left, true, false), new NullTest(right, true, false));
        var difference = Or(oneNull, And(neitherNull, new Comparison(left, SqlSyntax.AlternateNotEquals, right)));
        Predicate known = And(new Negation(absent), difference);
        Predicate result = Or(incomparable, known);
        return negated ? new Negation(result) : result;
    }

    private static Logical And(Predicate left, Predicate right) => new(left, SqlSyntax.And, right);
    private static Logical Or(Predicate left, Predicate right) => new(left, SqlSyntax.Or, right);
}
