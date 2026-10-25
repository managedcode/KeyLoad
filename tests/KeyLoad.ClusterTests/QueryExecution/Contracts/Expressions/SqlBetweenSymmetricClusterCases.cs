using KeyLoad.Query;

namespace KeyLoad.ClusterTests.QueryExecution;

internal static class SqlBetweenSymmetricClusterCases
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
    internal static IReadOnlyList<SqlBetweenSymmetricClusterCase> All
    {
        get;
    } =
    [
        new(() => new(SqlBetweenSymmetricClusterCases.Prefix + "(score BETWEEN 2 AND 1 OR score BETWEEN 1 AND 2)" + SqlBetweenSymmetricClusterCases.Tail), Both, ["high", "low"]),
        new(() => new(SqlBetweenSymmetricClusterCases.Prefix + "(score BETWEEN 1 AND 2 OR score BETWEEN 2 AND 1)" + SqlBetweenSymmetricClusterCases.Tail), Both, ["high", "low"]),
        new(() => new(SqlBetweenSymmetricClusterCases.Prefix + "NOT (score BETWEEN 2 AND 1 OR score BETWEEN 1 AND 2)" + SqlBetweenSymmetricClusterCases.Tail), new Negation(Both), []),
        new(() => new(SqlBetweenSymmetricClusterCases.Prefix + "NOT (score BETWEEN 2 AND 1 OR score BETWEEN 1 AND 2) /* original NOT */" + SqlBetweenSymmetricClusterCases.Tail), new Negation(Both), []),
        new(() => new(SqlBetweenSymmetricClusterCases.Prefix + "(score BETWEEN 1 AND 1 OR score BETWEEN 1 AND 1)" + SqlBetweenSymmetricClusterCases.Tail), Range(ValueOperand.Create(1), ValueOperand.Create(1)), ["low"]),
        new(() => new(SqlBetweenSymmetricClusterCases.Prefix + "NOT (score BETWEEN 1 AND 1 OR score BETWEEN 1 AND 1)" + SqlBetweenSymmetricClusterCases.Tail), new Negation(Range(ValueOperand.Create(1), ValueOperand.Create(1))), ["high"]),
        new(() => new(SqlBetweenSymmetricClusterCases.Prefix + "score BETWEEN 2 AND 1" + SqlBetweenSymmetricClusterCases.Tail), Reverse, []),
        new(() => new(Prefix + AsymmetricFault + Tail), Reverse, []),
        new(() => new(Prefix + AsymmetricHealthy + Tail), Both, ["high", "low"]),
        new(() => new(SqlBetweenSymmetricClusterCases.Prefix + "score NOT BETWEEN 2 AND 1 /* original asymmetric */" + SqlBetweenSymmetricClusterCases.Tail), new Negation(Reverse), ["high", "low"]),
        new(() => new(SqlBetweenSymmetricClusterCases.Prefix + "score BETWEEN 1 AND 1 /* original asymmetric */" + SqlBetweenSymmetricClusterCases.Tail), Range(ValueOperand.Create(1), ValueOperand.Create(1)), ["low"]),
        new(() => new(SqlBetweenSymmetricClusterCases.Prefix + "score NOT BETWEEN 1 AND 1 /* original asymmetric */" + SqlBetweenSymmetricClusterCases.Tail), new Negation(Range(ValueOperand.Create(1), ValueOperand.Create(1))), ["high"]),
        new(() => new(SqlBetweenSymmetricClusterCases.Prefix + "score BETWEEN NULL AND 2 /* original asymmetric */" + SqlBetweenSymmetricClusterCases.Tail), Range(ValueOperand.Create(null), ValueOperand.Create(2)), []),
        new(() => new(SqlBetweenSymmetricClusterCases.Prefix + "score NOT BETWEEN 2 AND 1" + SqlBetweenSymmetricClusterCases.Tail), new Negation(Reverse), ["high", "low"]),
        new(() => new(SqlBetweenSymmetricClusterCases.Prefix + "(score BETWEEN 2 AND 1 OR score BETWEEN 1 AND 2) OR label = 'missing'" + SqlBetweenSymmetricClusterCases.Tail), new Logical(Both, "OR", new Comparison(Label, "=", ValueOperand.Create("missing"))), ["high", "low", "missing"]),
        new(() => new(SqlBetweenSymmetricClusterCases.Prefix + "(score BETWEEN NULL AND 2 OR score BETWEEN 2 AND NULL)" + SqlBetweenSymmetricClusterCases.Tail), new Logical(Range(ValueOperand.Create(null), ValueOperand.Create(2)), "OR", Range(ValueOperand.Create(2), ValueOperand.Create(null))), []),
        new(() => new(SqlBetweenSymmetricClusterCases.Prefix + "(score BETWEEN score AND 2 OR score BETWEEN 2 AND score)" + SqlBetweenSymmetricClusterCases.Tail), new Logical(Range(Score, ValueOperand.Create(2)), "OR", Range(ValueOperand.Create(2), Score)), ["high", "low"])
    ];
    private static Logical Range(Operand lower, Operand upper) => new(
        new Comparison(Score, ">=", lower), "AND", new Comparison(Score, "<=", upper));
    internal static AstQueryRequest Ast(PartitionRef partition, SqlBetweenSymmetricClusterCase item)
        => new(partition, new(SqlNullOrderingClusterProtocol.Collection, null, [new("/label", "label")],
            item.Predicate, [new("/label", false)], SqlNullOrderingClusterProtocol.RowCount), AllowFullScan: true);
}
