namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlUnicodeIdentifierCases
{
    internal const string Profile = "SQL.UnicodeIdentifier.v1";
    private const int UnitValue = 1;
    internal const string Clause = "U&\"sc\\006fre\" = 1";
    internal const string WideClause = "u&\"sc\\+00006fre\" = 1";
    internal const string CustomClause = "U&\"sc!006fre\" UESCAPE '!' = 1";
    internal static IReadOnlyList<SqlNullDistinctCase> All { get; } =
    [
        new("U&\"sc\\006fre\" = 1", new global::KeyLoad.Query.Comparison(SqlNullDistinctCases.Score, "=", global::KeyLoad.Query.ValueOperand.Create(UnitValue)), ["low"]),
        new("u&\"sc\\+00006fre\" = 1", new global::KeyLoad.Query.Comparison(SqlNullDistinctCases.Score, "=", global::KeyLoad.Query.ValueOperand.Create(UnitValue)), ["low"]),
        new("U&\"sc!006fre\" UESCAPE '!' = 1", new global::KeyLoad.Query.Comparison(SqlNullDistinctCases.Score, "=", global::KeyLoad.Query.ValueOperand.Create(UnitValue)), ["low"]),
        new("U&\"sc\\006fre\" > 1", new global::KeyLoad.Query.Comparison(SqlNullDistinctCases.Score, ">", global::KeyLoad.Query.ValueOperand.Create(UnitValue)), ["high"]),
        new("U&\"sc\\006fre\" IS NULL", new global::KeyLoad.Query.NullTest(SqlNullDistinctCases.Score, false, false), ["null_a", "null_b"]),
        new("U&\"sc\\006fre\" IS NOT DISTINCT FROM 1", new global::KeyLoad.Query.Comparison(SqlNullDistinctCases.Score, "=", global::KeyLoad.Query.ValueOperand.Create(UnitValue)), ["low"]),
    ];
}
