using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed record SqlBooleanTruthCase(string Clause, Predicate Predicate, string[] Ids);
internal static class SqlBooleanTruthCases
{
    internal const string Profile = "TSQL.NamedParameters.v1";
    internal const string OperandProfile = "SQL.BooleanPredicate.v1";
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
    internal static IReadOnlyList<SqlBooleanTruthCase> All { get; } =
    [
        new("score = @truth", True, ["low"]),
        new("score = @false", False, ["high"]),
        new("score IS NULL", Null, ["null_a", "null_b"]),
        new("(score IS NULL OR score = @false)", Or(Null, False), ["high", "null_a", "null_b"]),
        new("(score IS NULL OR score = @truth)", Or(Null, True), ["low", "null_a", "null_b"]),
        new("score IS NOT DISTINCT FROM score AND score IS NOT NULL", And(Present, new NullTest(Score, true, false)), ["high", "low"]),
        new("(score IS NULL OR score = @false)", Or(Null, False), ["high", "null_a", "null_b"]),
        new("score IS NOT DISTINCT FROM score AND score IS NOT NULL", And(Present, new NullTest(Score, true, false)), ["high", "low"]),
        new("score = @truth OR label = 'missing'", Or(True, new Comparison(Label, "=", ValueOperand.Create("missing"))), ["low", "missing"]),
        new("score IS NULL OR label = 'missing'", Or(Null, new Comparison(Label, "=", ValueOperand.Create("missing"))), ["missing", "null_a", "null_b"]),
        new("score IS NOT DISTINCT FROM score AND score = @false", And(Present, False), ["high"]),
        new("score = @truth /* original bare */", True, ["low"]),
        new("NOT (score = @truth) /* original bare */", new Negation(True), ["high"]),
        new("(score = @truth)", True, ["low"]),
        new("NOT (score = @truth)", new Negation(True), ["high"]),
        new("@truth = @truth", new Comparison(ValueOperand.Create(true), "=", ValueOperand.Create(true)), ["high", "low", "missing", "null_a", "null_b"]),
        new("@false = @truth", new Comparison(ValueOperand.Create(false), "=", ValueOperand.Create(true)), []),
        new("@null = @truth", new Comparison(ValueOperand.Create(null), "=", ValueOperand.Create(true)), []),
        new("NOT (@null = @truth)", new Negation(new Comparison(ValueOperand.Create(null), "=", ValueOperand.Create(true))), []),
        new("score = @truth OR label = 'missing'", Or(True, new Comparison(Label, "=", ValueOperand.Create("missing"))), ["low", "missing"]),
        new("score = @truth AND NOT (@false = @truth)", And(True, new Negation(new Comparison(ValueOperand.Create(false), "=", ValueOperand.Create(true)))), ["low"]),
        new("@truth = @truth AND (score = @truth OR NOT (score = @truth))", And(new Comparison(ValueOperand.Create(true), "=", ValueOperand.Create(true)), Or(True, new Negation(True))), ["high", "low"]),
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
    private static Logical And(Predicate left, Predicate right) => new(left, "AND", right);
    private static Logical Or(Predicate left, Predicate right) => new(left, "OR", right);
    internal static AstQueryRequest Ast(PartitionRef partition, SqlBooleanTruthCase item)
        => new(partition, new(SqlNullOrderingProtocol.Collection, null, [new("/label", "label")],
            item.Predicate, [new("/label", false)], SqlNullOrderingProtocol.ResultCount), AllowFullScan: true);
}
