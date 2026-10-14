using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record SqlBetweenSymmetricRf3Case(string Clause, Predicate Predicate, string[] Ids);
internal static class SqlBetweenSymmetricRf3Cases
{
    internal const string Profile = "SQL.BetweenSymmetric.v1";
    internal const string AsymmetricFault = "score BETWEEN ASYMMETRIC 2 AND 1";
    internal const string AsymmetricHealthy = "score BETWEEN ASYMMETRIC 1 AND 2";
    internal const string Prefix = "SELECT label AS label FROM null_ordering WHERE ";
    internal const string Tail = " ORDER BY label ASC LIMIT 5";
    private static readonly FieldOperand Score = new("/score");
    private static readonly FieldOperand Label = new("/label");
    private static readonly Predicate Both = Range(ValueOperand.Create(1), ValueOperand.Create(2));
    private static readonly Predicate Reverse = Range(ValueOperand.Create(2), ValueOperand.Create(1));
    internal static IReadOnlyList<SqlBetweenSymmetricRf3Case> All
    {
        get;
    } =
    [
        new("score BETWEEN SYMMETRIC 2 AND 1", Both, ["high", "low"]),
        new("score BETWEEN SYMMETRIC 1 AND 2", Both, ["high", "low"]),
        new("score NOT BETWEEN SYMMETRIC 2 AND 1", new Negation(Both), []),
        new("NOT (score BETWEEN SYMMETRIC 2 AND 1)", new Negation(Both), []),
        new("score BETWEEN SYMMETRIC 1 AND 1", Range(ValueOperand.Create(1), ValueOperand.Create(1)), ["low"]),
        new("score NOT BETWEEN SYMMETRIC 1 AND 1", new Negation(Range(ValueOperand.Create(1), ValueOperand.Create(1))), ["high"]),
        new("score BETWEEN 2 AND 1", Reverse, []),
        new(AsymmetricFault, Reverse, []),
        new(AsymmetricHealthy, Both, ["high", "low"]),
        new("score NOT BETWEEN ASYMMETRIC 2 AND 1", new Negation(Reverse), ["high", "low"]),
        new("score BETWEEN ASYMMETRIC 1 AND 1", Range(ValueOperand.Create(1), ValueOperand.Create(1)), ["low"]),
        new("score NOT BETWEEN ASYMMETRIC 1 AND 1", new Negation(Range(ValueOperand.Create(1), ValueOperand.Create(1))), ["high"]),
        new("score BETWEEN ASYMMETRIC NULL AND 2", Range(ValueOperand.Create(null), ValueOperand.Create(2)), []),
        new("score NOT BETWEEN 2 AND 1", new Negation(Reverse), ["high", "low"]),
        new("score BETWEEN SYMMETRIC 2 AND 1 OR label = 'missing'", new Logical(Both, "OR", new Comparison(Label, "=", ValueOperand.Create("missing"))), ["high", "low", "missing"]),
        new("score BETWEEN SYMMETRIC NULL AND 2", new Logical(Range(ValueOperand.Create(null), ValueOperand.Create(2)), "OR", Range(ValueOperand.Create(2), ValueOperand.Create(null))), []),
        new("score BETWEEN SYMMETRIC score AND 2", new Logical(Range(Score, ValueOperand.Create(2)), "OR", Range(ValueOperand.Create(2), Score)), ["high", "low"])
    ];
    private static Logical Range(Operand lower, Operand upper) => new(
        new Comparison(Score, ">=", lower), "AND", new Comparison(Score, "<=", upper));
    internal static AstQueryRequest Ast(PartitionRef partition, SqlBetweenSymmetricRf3Case item)
        => new(partition, new(SqlNullOrderingRf3Protocol.Collection, null, [new("/label", "label")],
            item.Predicate, [new("/label", false)], SqlNullOrderingRf3Protocol.RowCount), AllowFullScan: true);
}
