using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlNullDistinctNullCases
{
    internal static IReadOnlyList<SqlNullDistinctCase> All
    {
        get;
    } =
    [
        new("score IS /* execution DISTINCT origin */ DISTINCT FROM NULL",
            SqlNullDistinctCases.And(new NullTest(SqlNullDistinctCases.Score, true, false),
                new NullTest(SqlNullDistinctCases.Score, true, true)), ["high", "low"]),
        new("[score] IS DISTINCT FROM NULL",
            SqlNullDistinctCases.And(new NullTest(SqlNullDistinctCases.Score, true, false),
                new NullTest(SqlNullDistinctCases.Score, true, true)), ["high", "low"]),
        new("score IS NOT NULL /* DISTINCT FROM NULL */",
            new NullTest(SqlNullDistinctCases.Score, true, false), ["high", "low", "missing"]),
        new("N'DISTINCT' IS NOT NULL", new NullTest(ValueOperand.Create("DISTINCT"), true, false),
            ["high", "low", "missing", "null_a", "null_b"]),
    ];
}
