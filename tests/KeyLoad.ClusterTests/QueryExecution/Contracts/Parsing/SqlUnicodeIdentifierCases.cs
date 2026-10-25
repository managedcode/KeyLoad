namespace KeyLoad.ClusterTests.QueryExecution;

internal static class SqlUnicodeIdentifierCases
{
    internal const string Profile = "SQL.UnicodeIdentifier";
    private const int UnitValue = 1;
    internal const string Clause = "\"score\" = 1 /* retained escaped identifier */";
    internal const string WideClause = "\"score\" = 1 /* retained wide identifier */";
    internal const string CustomClause = "\"score\" = 1 /* retained custom identifier */";
    internal static IReadOnlyList<SqlNullDistinctClusterCase> All
    {
        get;
    } =
    [
        new(() => new(SqlNullDistinctClusterCases.Prefix + "\"score\" = 1 /* retained escaped identifier */" + SqlNullDistinctClusterCases.Tail), new global::KeyLoad.Query.Comparison(SqlNullDistinctClusterCases.Score, "=", global::KeyLoad.Query.ValueOperand.Create(UnitValue)), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "\"score\" = 1 /* retained wide identifier */" + SqlNullDistinctClusterCases.Tail), new global::KeyLoad.Query.Comparison(SqlNullDistinctClusterCases.Score, "=", global::KeyLoad.Query.ValueOperand.Create(UnitValue)), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "\"score\" = 1 /* retained custom identifier */" + SqlNullDistinctClusterCases.Tail), new global::KeyLoad.Query.Comparison(SqlNullDistinctClusterCases.Score, "=", global::KeyLoad.Query.ValueOperand.Create(UnitValue)), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "\"score\" > 1" + SqlNullDistinctClusterCases.Tail), new global::KeyLoad.Query.Comparison(SqlNullDistinctClusterCases.Score, ">", global::KeyLoad.Query.ValueOperand.Create(UnitValue)), ["high"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "\"score\" IS NULL" + SqlNullDistinctClusterCases.Tail), new global::KeyLoad.Query.NullTest(SqlNullDistinctClusterCases.Score, false, false), ["null_a", "null_b"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "\"score\" IS NOT DISTINCT FROM 1" + SqlNullDistinctClusterCases.Tail), new global::KeyLoad.Query.Comparison(SqlNullDistinctClusterCases.Score, "=", global::KeyLoad.Query.ValueOperand.Create(UnitValue)), ["low"]),
    ];
}
