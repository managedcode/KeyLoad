namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class SqlUnicodeIdentifierCases
{
    internal const string Profile = "SQL.UnicodeIdentifier";
    private const int UnitValue = 1;
    internal const string Clause = "\"score\" = 1 /* retained escaped identifier */";
    internal const string WideClause = "\"score\" = 1 /* retained wide identifier */";
    internal const string CustomClause = "\"score\" = 1 /* retained custom identifier */";
    internal static IReadOnlyList<SqlNullDistinctCase> All
    {
        get;
    } =
    [
        new("\"score\" = 1 /* retained escaped identifier */", new global::KeyLoad.Query.Comparison(SqlNullDistinctCases.Score, "=", global::KeyLoad.Query.ValueOperand.Create(UnitValue)), ["low"]),
        new("\"score\" = 1 /* retained wide identifier */", new global::KeyLoad.Query.Comparison(SqlNullDistinctCases.Score, "=", global::KeyLoad.Query.ValueOperand.Create(UnitValue)), ["low"]),
        new("\"score\" = 1 /* retained custom identifier */", new global::KeyLoad.Query.Comparison(SqlNullDistinctCases.Score, "=", global::KeyLoad.Query.ValueOperand.Create(UnitValue)), ["low"]),
        new("\"score\" > 1", new global::KeyLoad.Query.Comparison(SqlNullDistinctCases.Score, ">", global::KeyLoad.Query.ValueOperand.Create(UnitValue)), ["high"]),
        new("\"score\" IS NULL", new global::KeyLoad.Query.NullTest(SqlNullDistinctCases.Score, false, false), ["null_a", "null_b"]),
        new("\"score\" IS NOT DISTINCT FROM 1", new global::KeyLoad.Query.Comparison(SqlNullDistinctCases.Score, "=", global::KeyLoad.Query.ValueOperand.Create(UnitValue)), ["low"]),
    ];
}
