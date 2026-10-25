using KeyLoad.Query;

namespace KeyLoad.ClusterTests.QueryExecution;

internal static class SqlBooleanTruthClusterCases
{
    internal const string Profile = "TSQL.NamedParameters";
    internal const string OperandProfile = "SQL.BooleanPredicate";
    internal const string BareClause = "score = @truth /* original bare */";
    internal const string NegatedBareClause = "NOT (score = @truth) /* original bare */";
    internal const string Prefix = "SELECT TOP (5) label AS label FROM null_ordering WHERE ";
    internal const string Tail = " ORDER BY label ASC";
    private static readonly FieldOperand Score = new("/score");
    private static readonly FieldOperand Label = new("/label");
    private static readonly Predicate True = new Comparison(Score, "=", ValueOperand.Create(true));
    private static readonly Predicate False = new Comparison(Score, "=", ValueOperand.Create(false));
    private static readonly Predicate Null = new NullTest(Score, false, false);
    private static readonly Predicate Present = new NullTest(Score, true, true);
    internal static IReadOnlyList<SqlBooleanTruthClusterCase> All
    {
        get;
    } =
    [
        new(() => new(SqlBooleanTruthClusterCases.Prefix + "score = @truth" + SqlBooleanTruthClusterCases.Tail), True, ["low"]),
        new(() => new(SqlBooleanTruthClusterCases.Prefix + "score = @false" + SqlBooleanTruthClusterCases.Tail), False, ["high"]),
        new(() => new(SqlBooleanTruthClusterCases.Prefix + "score IS NULL" + SqlBooleanTruthClusterCases.Tail), Null, ["null_a", "null_b"]),
        new(() => new(SqlBooleanTruthClusterCases.Prefix + "(score IS NULL OR score = @false)" + SqlBooleanTruthClusterCases.Tail), Or(Null, False), ["high", "null_a", "null_b"]),
        new(() => new(SqlBooleanTruthClusterCases.Prefix + "(score IS NULL OR score = @truth)" + SqlBooleanTruthClusterCases.Tail), Or(Null, True), ["low", "null_a", "null_b"]),
        new(() => new(SqlBooleanTruthClusterCases.Prefix + "score IS NOT DISTINCT FROM score AND score IS NOT NULL" + SqlBooleanTruthClusterCases.Tail), And(Present, new NullTest(Score, true, false)), ["high", "low"]),
        new(() => new(SqlBooleanTruthClusterCases.Prefix + "(score IS NULL OR score = @false)" + SqlBooleanTruthClusterCases.Tail), Or(Null, False), ["high", "null_a", "null_b"]),
        new(() => new(SqlBooleanTruthClusterCases.Prefix + "score IS NOT DISTINCT FROM score AND score IS NOT NULL" + SqlBooleanTruthClusterCases.Tail), And(Present, new NullTest(Score, true, false)), ["high", "low"]),
        new(() => new(SqlBooleanTruthClusterCases.Prefix + "score = @truth OR label = 'missing'" + SqlBooleanTruthClusterCases.Tail), Or(True, new Comparison(Label, "=", ValueOperand.Create("missing"))), ["low", "missing"]),
        new(() => new(SqlBooleanTruthClusterCases.Prefix + "score IS NULL OR label = 'missing'" + SqlBooleanTruthClusterCases.Tail), Or(Null, new Comparison(Label, "=", ValueOperand.Create("missing"))), ["missing", "null_a", "null_b"]),
        new(() => new(SqlBooleanTruthClusterCases.Prefix + "score IS NOT DISTINCT FROM score AND score = @false" + SqlBooleanTruthClusterCases.Tail), And(Present, False), ["high"]),
        new(() => new(SqlBooleanTruthClusterCases.Prefix + "score = @truth /* original bare */" + SqlBooleanTruthClusterCases.Tail), True, ["low"]),
        new(() => new(SqlBooleanTruthClusterCases.Prefix + "NOT (score = @truth) /* original bare */" + SqlBooleanTruthClusterCases.Tail), new Negation(True), ["high"]),
        new(() => new(SqlBooleanTruthClusterCases.Prefix + "(score = @truth)" + SqlBooleanTruthClusterCases.Tail), True, ["low"]),
        new(() => new(SqlBooleanTruthClusterCases.Prefix + "NOT (score = @truth)" + SqlBooleanTruthClusterCases.Tail), new Negation(True), ["high"]),
        new(() => new(SqlBooleanTruthClusterCases.Prefix + "@truth = @truth" + SqlBooleanTruthClusterCases.Tail), new Comparison(ValueOperand.Create(true), "=", ValueOperand.Create(true)), ["high", "low", "missing", "null_a", "null_b"]),
        new(() => new(SqlBooleanTruthClusterCases.Prefix + "@false = @truth" + SqlBooleanTruthClusterCases.Tail), new Comparison(ValueOperand.Create(false), "=", ValueOperand.Create(true)), []),
        new(() => new(SqlBooleanTruthClusterCases.Prefix + "@null = @truth" + SqlBooleanTruthClusterCases.Tail), new Comparison(ValueOperand.Create(null), "=", ValueOperand.Create(true)), []),
        new(() => new(SqlBooleanTruthClusterCases.Prefix + "NOT (@null = @truth)" + SqlBooleanTruthClusterCases.Tail), new Negation(new Comparison(ValueOperand.Create(null), "=", ValueOperand.Create(true))), []),
        new(() => new(SqlBooleanTruthClusterCases.Prefix + "score = @truth OR label = 'missing'" + SqlBooleanTruthClusterCases.Tail), Or(True, new Comparison(Label, "=", ValueOperand.Create("missing"))), ["low", "missing"]),
        new(() => new(SqlBooleanTruthClusterCases.Prefix + "score = @truth AND NOT (@false = @truth)" + SqlBooleanTruthClusterCases.Tail), And(True, new Negation(new Comparison(ValueOperand.Create(false), "=", ValueOperand.Create(true)))), ["low"]),
        new(() => new(SqlBooleanTruthClusterCases.Prefix + "@truth = @truth AND (score = @truth OR NOT (score = @truth))" + SqlBooleanTruthClusterCases.Tail), And(new Comparison(ValueOperand.Create(true), "=", ValueOperand.Create(true)), Or(True, new Negation(True))), ["high", "low"]),
    ];
    internal static Dictionary<string, global::System.Text.Json.JsonElement> Parameters()
        => new(StringComparer.Ordinal)
        {
            [TrueParameter] = global::System.Text.Json.JsonSerializer.SerializeToElement(true),
            [FalseParameter] = global::System.Text.Json.JsonSerializer.SerializeToElement(false),
            [NullParameter] = global::System.Text.Json.JsonSerializer.SerializeToElement((bool?)null)
        };
    internal const string TrueParameter = "truth";
    private const string FalseParameter = "false";
    private const string NullParameter = "null";
    internal static global::Npgsql.NpgsqlParameter[] WireParameters()
        => [new() { ParameterName = TrueParameter, NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.Boolean, Value = true },
            new() { ParameterName = FalseParameter, NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.Boolean, Value = false },
            new() { ParameterName = NullParameter, NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.Boolean, Value = DBNull.Value }];
    private static Logical And(Predicate left, Predicate right) => new(left, "AND", right);
    private static Logical Or(Predicate left, Predicate right) => new(left, "OR", right);
    internal static AstQueryRequest Ast(PartitionRef partition, SqlBooleanTruthClusterCase item)
        => new(partition, new(SqlNullOrderingClusterProtocol.Collection, null, [new("/label", "label")],
            item.Predicate, [new("/label", false)], SqlNullOrderingClusterProtocol.RowCount), AllowFullScan: true);
}
