using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record SqlNullDistinctRf3Case(string Clause, Predicate Predicate, string[] Ids);
internal static class SqlNullDistinctRf3Cases
{
    internal const string NumericSeparatorProfile = "SQL.NumericDigitSeparators.v1";
    internal const string NumericSeparatorClause = "score = 1_0e-1";
    internal const string NumericRadixSeparatorClause = "score = 0b_1";
    internal const string NumericOctalSeparatorClause = "score = 0o_1";
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
    internal const string UnicodeProfile = "SQL.UnicodeStringLiteral.v1";
    internal const string UnicodeClause = "score = 1 AND label = U&'l\\006fw'";
    internal const string UnicodeCustomClause = "score = 1 AND label = U&'l#006fw' UESCAPE '#'";
    internal const string BinaryOctalProfile = "SQL.BinaryOctalIntegerLiteral.v1";
    internal const string BinaryClause = "score = 0b1";
    internal const string OctalClause = "score = 0o1";
    internal const string EscapeProfile = "SQL.EscapeStringLiteral.v1";
    internal const string DollarProfile = "SQL.DollarQuotedString.v1";
    internal const string DollarClause = "score = 1 AND label = $tag$low$tag$";
    internal const string EscapeClause = "score = 1 AND label = E'low'";
    internal const string ContinuationClause = "score = 1 AND label = 'lo'\n'w'";
    private const decimal Half = 0.5m;
    internal const string Prefix = "SELECT label AS label FROM null_ordering WHERE ";
    internal const string Tail = " ORDER BY label ASC LIMIT 5";
    internal static readonly FieldOperand Score = new("/score");
    internal static readonly FieldOperand Label = new("/label");
    internal static IReadOnlyList<SqlNullDistinctRf3Case> All
    {
        get;
    } =
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
        new(DollarClause, And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(Label, "=", ValueOperand.Create("low"))), ["low"]),
        new("label = $$low$$", new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new("label = $κ_1$low$κ_1$", new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new("label = $X$low$X$", new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new("label = $$$$", new Comparison(Label, "=", ValueOperand.Create("")), []),
        new("label = $$l\\ow$$", new Comparison(Label, "=", ValueOperand.Create("l\\ow")), []),
        new("label = $$' OR score = 1 --$$", new Comparison(Label, "=", ValueOperand.Create("' OR score = 1 --")), []),
        new("label = $outer$$inner$low$inner$$outer$", new Comparison(Label, "=", ValueOperand.Create("$inner$low$inner$")), []),
        new("score = 1 AND $$😀$$ = '😀'", And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create("😀"), "=", ValueOperand.Create("😀"))), ["low"]),
        new("score = 0b1", new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new("score = +0B1", new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new("score = -0b1", new Comparison(Score, "=", ValueOperand.Create(-1)), []),
        new("score > 0b1", new Comparison(Score, ">", ValueOperand.Create(1)), ["high"]),
        new("score = 0o1", new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new("score = +0O1", new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new("score = -0o1", new Comparison(Score, "=", ValueOperand.Create(-1)), []),
        new("score > 0o1", new Comparison(Score, ">", ValueOperand.Create(1)), ["high"]),
        new("score = 0b111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111", new Comparison(Score, "=", ValueOperand.Create(decimal.MaxValue)), []),
        new("score = 0o77777777777777777777777777777777", new Comparison(Score, "=", ValueOperand.Create(decimal.MaxValue)), []),
        new("score BETWEEN 0b1 AND 0o2", And(new Comparison(Score, ">=", ValueOperand.Create(1)),
            new Comparison(Score, "<=", ValueOperand.Create(2))), ["high", "low"]),
        new("score IN (0b1,0o2)", new InPredicate(Score, [ValueOperand.Create(1), ValueOperand.Create(2)], false), ["high", "low"]),
        new("score IS NOT DISTINCT FROM 0b1", new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new("score = 1 AND 0b111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111 = 79228162514264337593543950335", And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create(decimal.MaxValue), "=", ValueOperand.Create(decimal.MaxValue))), ["low"]),
        new("score = 1 AND 0o77777777777777777777777777777777 = 79228162514264337593543950335", And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create(decimal.MaxValue), "=", ValueOperand.Create(decimal.MaxValue))), ["low"]),
        new("score = 1 AND -0b111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111 = -79228162514264337593543950335", And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create(decimal.MinValue), "=", ValueOperand.Create(decimal.MinValue))), ["low"]),
        new("score = 1 AND -0o77777777777777777777777777777777 = -79228162514264337593543950335", And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create(decimal.MinValue), "=", ValueOperand.Create(decimal.MinValue))), ["low"]),
        new("score = 1 AND label = U&'l\\006fw'", And(new Comparison(Score, "=", ValueOperand.Create(1)), new Comparison(Label, "=", ValueOperand.Create("low"))), ["low"]),
        new("score = 1 AND label = U&'l#006fw' UESCAPE '#'", And(new Comparison(Score, "=", ValueOperand.Create(1)), new Comparison(Label, "=", ValueOperand.Create("low"))), ["low"]),
        new("label = u&'l\\+00006fw'", new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new("label = U&'lo'\n'w'", new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new("label = U&'l!006fw' UESCAPE E'\\x21'", new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new("label = U&'l!006fw' UESCAPE $$!$$", new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new("score = 1 AND U&'\\+01F600' = '😀'", And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create("😀"), "=", ValueOperand.Create("😀"))), ["low"]),
        new("score = 1 AND U&'\\D83D\\+00DE00' = '😀'", And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create("😀"), "=", ValueOperand.Create("😀"))), ["low"]),
        new("score = 1 AND U&'\\+00D83D\\DE00' = '😀'", And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create("😀"), "=", ValueOperand.Create("😀"))), ["low"]),
        new("label = U&'\\\\'", new Comparison(Label, "=", ValueOperand.Create("\\")), []),
        new("label = U&'low''other'", new Comparison(Label, "=", ValueOperand.Create("low'other")), []),
        new("score = 1_0e-1", new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new("score = .0_1e+0_2", new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new("score = 1_0.0_0e-0_1", new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new("score = +0x_1", new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new("score = 0b_1", new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new("score = 0o_1", new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new("score = -0o_1", new Comparison(Score, "=", ValueOperand.Create(-1)), []),
        new("score = 0x1_0", new Comparison(Score, "=", ValueOperand.Create(16)), []),
        new("score = 0b1_0", new Comparison(Score, "=", ValueOperand.Create(2)), ["high"]),
        new("score = 0o1_0", new Comparison(Score, "=", ValueOperand.Create(8)), []),
        new("score = 79_228_162_514_264_337_593_543_950_335", new Comparison(Score, "=", ValueOperand.Create(decimal.MaxValue)), []),
        new("score = -79_228_162_514_264_337_593_543_950_335", new Comparison(Score, "=", ValueOperand.Create(decimal.MinValue)), []),
        new("score = 0x_FFFF_FFFF_FFFF_FFFF_FFFF_FFFF", new Comparison(Score, "=", ValueOperand.Create(decimal.MaxValue)), []),
        new("score = 0b11111111_11111111_11111111_11111111_11111111_11111111_11111111_11111111_11111111_11111111_11111111_11111111", new Comparison(Score, "=", ValueOperand.Create(decimal.MaxValue)), []),
        new("score = 0o7777_7777_7777_7777_7777_7777_7777_7777", new Comparison(Score, "=", ValueOperand.Create(decimal.MaxValue)), []),
        .. SqlUnicodeIdentifierCases.All,
    ];
    internal static Logical And(Predicate left, Predicate right) => new(left, "AND", right);
    internal static Logical Or(Predicate left, Predicate right) => new(left, "OR", right);
    internal static AstQueryRequest Ast(PartitionRef partition, SqlNullDistinctRf3Case item)
        => new(partition, new(SqlNullOrderingRf3Protocol.Collection, null, [new("/label", "label")],
            item.Predicate, [new("/label", false)], SqlNullOrderingRf3Protocol.RowCount), AllowFullScan: true);
}
