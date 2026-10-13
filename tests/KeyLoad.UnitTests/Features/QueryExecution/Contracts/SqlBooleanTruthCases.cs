using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed record SqlBooleanTruthCase(string Clause, Predicate Predicate, string[] Ids);
internal static class SqlBooleanTruthCases
{
    internal const string Profile = "SQL.BooleanTruth.v1";
    internal const string Prefix = "SELECT label AS label FROM null_ordering WHERE ";
    internal const string Tail = " ORDER BY label ASC LIMIT 5";
    private static readonly FieldOperand Score = new("/score");
    private static readonly FieldOperand Label = new("/label");
    private static readonly Predicate True = new Comparison(Score, "=", ValueOperand.Create(true));
    private static readonly Predicate False = new Comparison(Score, "=", ValueOperand.Create(false));
    private static readonly Predicate Null = new NullTest(Score, false, false);
    private static readonly Predicate Present = new NullTest(Score, true, true);
    internal static IReadOnlyList<SqlBooleanTruthCase> All { get; } =
    [
        new("score IS TRUE", True, ["low"]),
        new("score IS FALSE", False, ["high"]),
        new("score IS UNKNOWN", Null, ["null_a", "null_b"]),
        new("score IS NOT TRUE", Or(Null, False), ["high", "null_a", "null_b"]),
        new("score IS NOT FALSE", Or(Null, True), ["low", "null_a", "null_b"]),
        new("score IS NOT UNKNOWN", And(Present, new NullTest(Score, true, false)), ["high", "low"]),
        new("NOT (score IS TRUE)", Or(Null, False), ["high", "null_a", "null_b"]),
        new("NOT (score IS UNKNOWN)", And(Present, new NullTest(Score, true, false)), ["high", "low"]),
        new("score IS TRUE OR label = 'missing'", Or(True, new Comparison(Label, "=", ValueOperand.Create("missing"))), ["low", "missing"]),
        new("score IS UNKNOWN OR label = 'missing'", Or(Null, new Comparison(Label, "=", ValueOperand.Create("missing"))), ["missing", "null_a", "null_b"]),
        new("score IS NOT UNKNOWN AND score IS FALSE", And(Present, False), ["high"])
    ];
    private static Logical And(Predicate left, Predicate right) => new(left, "AND", right);
    private static Logical Or(Predicate left, Predicate right) => new(left, "OR", right);
    internal static AstQueryRequest Ast(PartitionRef partition, SqlBooleanTruthCase item)
        => new(partition, new(SqlNullOrderingProtocol.Collection, null, [new("/label", "label")],
            item.Predicate, [new("/label", false)], SqlNullOrderingProtocol.ResultCount), AllowFullScan: true);
}
