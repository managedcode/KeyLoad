using KeyLoad.Query;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class SqlBetweenSymmetricCases
{
    internal const string Profile = "SQL.BetweenSymmetric";
    internal const string AsymmetricFault = "score BETWEEN 2 AND 1 /* original asymmetric */";
    internal const string AsymmetricHealthy = "score BETWEEN 1 AND 2 /* original asymmetric */";
    internal const string Prefix = "SELECT TOP (5) label AS label FROM null_ordering WHERE ";
    internal const string Tail = " ORDER BY label ASC";
    private static readonly FieldOperand Score = new("/score");
    private static readonly FieldOperand Label = new("/label");
    private static readonly Predicate Both = Range(ValueOperand.Create(1), ValueOperand.Create(2));
    private static readonly Predicate Reverse = Range(ValueOperand.Create(2), ValueOperand.Create(1));
    internal static IReadOnlyList<SqlBetweenSymmetricCase> All
    {
        get;
    } =
    [
        new("(score BETWEEN 2 AND 1 OR score BETWEEN 1 AND 2)", Both, ["high", "low"]),
        new("(score BETWEEN 1 AND 2 OR score BETWEEN 2 AND 1)", Both, ["high", "low"]),
        new("NOT (score BETWEEN 2 AND 1 OR score BETWEEN 1 AND 2)", new Negation(Both), []),
        new("NOT (score BETWEEN 2 AND 1 OR score BETWEEN 1 AND 2) /* original NOT */", new Negation(Both), []),
        new("(score BETWEEN 1 AND 1 OR score BETWEEN 1 AND 1)", Range(ValueOperand.Create(1), ValueOperand.Create(1)), ["low"]),
        new("NOT (score BETWEEN 1 AND 1 OR score BETWEEN 1 AND 1)", new Negation(Range(ValueOperand.Create(1), ValueOperand.Create(1))), ["high"]),
        new("score BETWEEN 2 AND 1", Reverse, []),
        new(AsymmetricFault, Reverse, []),
        new(AsymmetricHealthy, Both, ["high", "low"]),
        new("score NOT BETWEEN 2 AND 1 /* original asymmetric */", new Negation(Reverse), ["high", "low"]),
        new("score BETWEEN 1 AND 1 /* original asymmetric */", Range(ValueOperand.Create(1), ValueOperand.Create(1)), ["low"]),
        new("score NOT BETWEEN 1 AND 1 /* original asymmetric */", new Negation(Range(ValueOperand.Create(1), ValueOperand.Create(1))), ["high"]),
        new("score BETWEEN NULL AND 2 /* original asymmetric */", Range(ValueOperand.Create(null), ValueOperand.Create(2)), []),
        new("score NOT BETWEEN 2 AND 1", new Negation(Reverse), ["high", "low"]),
        new("(score BETWEEN 2 AND 1 OR score BETWEEN 1 AND 2) OR label = 'missing'", new Logical(Both, "OR", new Comparison(Label, "=", ValueOperand.Create("missing"))), ["high", "low", "missing"]),
        new("(score BETWEEN NULL AND 2 OR score BETWEEN 2 AND NULL)", new Logical(Range(ValueOperand.Create(null), ValueOperand.Create(2)), "OR", Range(ValueOperand.Create(2), ValueOperand.Create(null))), []),
        new("(score BETWEEN score AND 2 OR score BETWEEN 2 AND score)", new Logical(Range(Score, ValueOperand.Create(2)), "OR", Range(ValueOperand.Create(2), Score)), ["high", "low"])
    ];
    private static Logical Range(Operand lower, Operand upper) => new(
        new Comparison(Score, ">=", lower), "AND", new Comparison(Score, "<=", upper));
    internal static AstQueryRequest Ast(PartitionRef partition, SqlBetweenSymmetricCase item)
        => new(partition, new(SqlNullOrderingProtocol.Collection, null, [new("/label", "label")],
            item.Predicate, [new("/label", false)], SqlNullOrderingProtocol.ResultCount), AllowFullScan: true);
}
