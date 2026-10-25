using KeyLoad.Query;

namespace KeyLoad.ClusterTests.QueryExecution;

internal static class SqlNullDistinctClusterNullCases
{
    internal static IReadOnlyList<SqlNullDistinctClusterCase> All
    {
        get;
    } =
    [
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score IS /* execution DISTINCT origin */ DISTINCT FROM NULL" + SqlNullDistinctClusterCases.Tail),
            SqlNullDistinctClusterCases.And(new NullTest(SqlNullDistinctClusterCases.Score, true, false),
                new NullTest(SqlNullDistinctClusterCases.Score, true, true)), ["high", "low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "[score] IS DISTINCT FROM NULL" + SqlNullDistinctClusterCases.Tail),
            SqlNullDistinctClusterCases.And(new NullTest(SqlNullDistinctClusterCases.Score, true, false),
                new NullTest(SqlNullDistinctClusterCases.Score, true, true)), ["high", "low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score IS NOT NULL /* DISTINCT FROM NULL */" + SqlNullDistinctClusterCases.Tail),
            new NullTest(SqlNullDistinctClusterCases.Score, true, false), ["high", "low", "missing"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "N'DISTINCT' IS NOT NULL" + SqlNullDistinctClusterCases.Tail), new NullTest(ValueOperand.Create("DISTINCT"), true, false),
            ["high", "low", "missing", "null_a", "null_b"]),
    ];
}
