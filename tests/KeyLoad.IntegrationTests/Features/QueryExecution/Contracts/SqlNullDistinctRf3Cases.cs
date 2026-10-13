using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record SqlNullDistinctRf3Case(string Clause, Predicate Predicate, string[] Ids);
internal static class SqlNullDistinctRf3Cases
{
    internal const string Profile = "SQL.NullDistinct.v1";
    internal const string Prefix = "SELECT label AS label FROM null_ordering WHERE ";
    internal const string Tail = " ORDER BY label ASC LIMIT 5";
    internal static readonly FieldOperand Score = new("/score");
    internal static readonly FieldOperand Label = new("/label");
    internal static IReadOnlyList<SqlNullDistinctRf3Case> All { get; } =
    [
        new("score IS DISTINCT FROM NULL", And(new NullTest(Score, true, false), new NullTest(Score, true, true)), ["high", "low"]),
        new("score IS NOT DISTINCT FROM NULL", new NullTest(Score, false, false), ["null_a", "null_b"]),
        new("score IS DISTINCT FROM 1", Or(new NullTest(Score, false, false), new Comparison(Score, "<>", ValueOperand.Create(1))), ["high", "null_a", "null_b"]),
        new("score IS NOT DISTINCT FROM 1", new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new("score IS DISTINCT FROM score", new Comparison(Score, "<>", Score), []),
        new("score IS NOT DISTINCT FROM score", new NullTest(Score, true, true), ["high", "low", "null_a", "null_b"]),
        new("NOT (score IS DISTINCT FROM NULL)", new NullTest(Score, false, false), ["null_a", "null_b"]),
        new("score IS DISTINCT FROM 1 OR label = 'missing'", Or(Or(new NullTest(Score, false, false),
            new Comparison(Score, "<>", ValueOperand.Create(1))), new Comparison(Label, "=", ValueOperand.Create("missing"))),
            ["high", "missing", "null_a", "null_b"])
    ];
    internal static Logical And(Predicate left, Predicate right) => new(left, "AND", right);
    internal static Logical Or(Predicate left, Predicate right) => new(left, "OR", right);
    internal static AstQueryRequest Ast(PartitionRef partition, SqlNullDistinctRf3Case item)
        => new(partition, new(SqlNullOrderingRf3Protocol.Collection, null, [new("/label", "label")],
            item.Predicate, [new("/label", false)], SqlNullOrderingRf3Protocol.RowCount), AllowFullScan: true);
}
