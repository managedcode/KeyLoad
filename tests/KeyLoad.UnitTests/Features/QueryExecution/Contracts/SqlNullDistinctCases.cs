using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed record SqlNullDistinctCase(string Clause, Predicate Predicate, string[] Ids);
internal static class SqlNullDistinctCases
{
    internal const string Profile = "SQL.NullDistinct.v1";
    internal const string NullSynonymProfile = "SQL.NullPredicateSynonyms.v1";
    internal const string NullClause = "score ISNULL";
    internal const string NotNullClause = "score NOTNULL";
    internal const string PositiveProfile = "SQL.PositiveNumericLiteral.v1";
    internal const string PositiveClause = "score = +1";
    internal const string LeadingProfile = "SQL.LeadingDecimalLiteral.v1";
    internal const string LeadingClause = "score > .5";
    internal const string HexProfile = "SQL.HexadecimalIntegerLiteral.v1";
    internal const string HexClause = "score = 0x1";
    internal const string EscapeProfile = "SQL.EscapeStringLiteral.v1";
    internal const string EscapeClause = "score = 1 AND label = E'low'";
    internal const string ContinuationClause = "score = 1 AND label = 'lo'\n'w'";
    private const decimal Half = 0.5m;
    internal const string Prefix = "SELECT label AS label FROM null_ordering WHERE ";
    internal const string Tail = " ORDER BY label ASC LIMIT 5";
    internal static readonly FieldOperand Score = new("/score");
    internal static readonly FieldOperand Label = new("/label");
    internal static IReadOnlyList<SqlNullDistinctCase> All { get; } =
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
            ["high", "missing", "null_a", "null_b"]),
        new("score IS NULL", new NullTest(Score, false, false), ["null_a", "null_b"]),
        new(NullClause, new NullTest(Score, false, false), ["null_a", "null_b"]),
        new("score IS NOT NULL", new NullTest(Score, true, false), ["high", "low", "missing"]),
        new(NotNullClause, new NullTest(Score, true, false), ["high", "low", "missing"]),
        new("NOT (score ISNULL)", new Negation(new NullTest(Score, false, false)), ["high", "low", "missing"]),
        new("NOT (score NOTNULL)", new Negation(new NullTest(Score, true, false)), ["null_a", "null_b"]),
        new("score ISNULL OR label = 'missing'", Or(new NullTest(Score, false, false),
            new Comparison(Label, "=", ValueOperand.Create("missing"))), ["missing", "null_a", "null_b"]),
        new(PositiveClause, new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new("score > +1", new Comparison(Score, ">", ValueOperand.Create(1)), ["high"]),
        new("score BETWEEN +1 AND +2", And(new Comparison(Score, ">=", ValueOperand.Create(1)),
            new Comparison(Score, "<=", ValueOperand.Create(2))), ["high", "low"]),
        new("score IN (+1,+2)", new InPredicate(Score, [ValueOperand.Create(1), ValueOperand.Create(2)], false), ["high", "low"]),
        new("score IS NOT DISTINCT FROM +1", new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new("score = +1e+0", new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new("score = -1", new Comparison(Score, "=", ValueOperand.Create(-1)), []),
        new("score = +79228162514264337593543950335", new Comparison(Score, "=",
            ValueOperand.Create(decimal.MaxValue)), []),
        new(LeadingClause, new Comparison(Score, ">", ValueOperand.Create(Half)), ["high", "low"]),
        new("score = .1e1", new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new("score > -.5", new Comparison(Score, ">", ValueOperand.Create(-Half)), ["high", "low"]),
        new("score > +.5", new Comparison(Score, ">", ValueOperand.Create(Half)), ["high", "low"]),
        new("score BETWEEN .5 AND 1.", And(new Comparison(Score, ">=", ValueOperand.Create(Half)),
            new Comparison(Score, "<=", ValueOperand.Create(1))), ["low"]),
        new("score IN (.5,1.)", new InPredicate(Score, [ValueOperand.Create(Half), ValueOperand.Create(1)], false), ["low"]),
        new("score = .5", new Comparison(Score, "=", ValueOperand.Create(Half)), []),
        new("score IS NOT DISTINCT FROM .1e1", new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new(HexClause, new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new("score > 0X1", new Comparison(Score, ">", ValueOperand.Create(1)), ["high"]),
        new("score = +0x1", new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new("score = -0x1", new Comparison(Score, "=", ValueOperand.Create(-1)), []),
        new("score BETWEEN 0x1 AND 0x2", And(new Comparison(Score, ">=", ValueOperand.Create(1)),
            new Comparison(Score, "<=", ValueOperand.Create(2))), ["high", "low"]),
        new("score IN (0x1,0x2)", new InPredicate(Score, [ValueOperand.Create(1), ValueOperand.Create(2)], false), ["high", "low"]),
        new("score IS NOT DISTINCT FROM 0x1", new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new("score = 0xFFFFFFFFFFFFFFFFFFFFFFFF", new Comparison(Score, "=", ValueOperand.Create(decimal.MaxValue)), []),
        new("label = '0x1'", new Comparison(Label, "=", ValueOperand.Create("0x1")), []),
        new(EscapeClause, And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(Label, "=", ValueOperand.Create("low"))), ["low"]),
        new("label = E'\\x6c\\x6f\\x77'", new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new("label = e'\\154\\157\\167'", new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new("label = E'\\u006c\\u006f\\u0077'", new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new("score = 1 AND E'\\U0001f600' = '😀'", And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create("😀"), "=", ValueOperand.Create("😀"))), ["low"]),
        new("score = 1 AND E'\\uD83D\\uDE00' = '😀'", And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create("😀"), "=", ValueOperand.Create("😀"))), ["low"]),
        new("score = 1 AND E'\\xf0\\x9f\\x98\\x80' = '😀'", And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create("😀"), "=", ValueOperand.Create("😀"))), ["low"]),
        new("label = E'\\'$1'", new Comparison(Label, "=", ValueOperand.Create("'$1")), []),
        new("label = E'\\' OR score = 1 --'", new Comparison(Label, "=", ValueOperand.Create("' OR score = 1 --")), []),
        new("score = 1 AND E'\\b\\f\\n\\r\\t\\\\' = '\b\f\n\r\t\\'",
            And(new Comparison(Score, "=", ValueOperand.Create(1)),
                new Comparison(ValueOperand.Create("\b\f\n\r\t\\"), "=", ValueOperand.Create("\b\f\n\r\t\\"))), ["low"]),
        new("label = E'lo\\w'", new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new("score = 1 AND E'\\x' = 'x'", And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create("x"), "=", ValueOperand.Create("x"))), ["low"]),
        new("score = 1 AND E'\\U0000D83D\\uDE00' = '😀'", And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create("😀"), "=", ValueOperand.Create("😀"))), ["low"]),
        new("score = 1 AND E'\\uD83D\\U0000DE00' = '😀'", And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create("😀"), "=", ValueOperand.Create("😀"))), ["low"]),
        new("label = 'lo'\n'w'", new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new("label = 'lo'\r'w'", new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new("label = 'lo'\r\n'w'", new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new("label = 'lo' --fragment\n 'w'", new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new("label = E'lo'\n'\\x77'", new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new(ContinuationClause, And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(Label, "=", ValueOperand.Create("low"))), ["low"]),
        new("score = 1 AND E'\\xc3'\n'\\xa9' = 'é'", And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create("é"), "=", ValueOperand.Create("é"))), ["low"]),
        new("score = 1 AND E'\\xf0\\x9f'\n'\\x98\\x80' = '😀'", And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create("😀"), "=", ValueOperand.Create("😀"))), ["low"]),
    ];
    internal static Logical And(Predicate left, Predicate right) => new(left, "AND", right);
    internal static Logical Or(Predicate left, Predicate right) => new(left, "OR", right);
    internal static AstQueryRequest Ast(PartitionRef partition, SqlNullDistinctCase item)
        => new(partition, new(SqlNullOrderingProtocol.Collection, null, [new("/label", "label")],
            item.Predicate, [new("/label", false)], SqlNullOrderingProtocol.ResultCount), AllowFullScan: true);
}
