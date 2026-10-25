using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class SqlNullDistinctCases
{
    internal const string NumericSeparatorProfile = "SQL.NumericDigitSeparators";
    internal const string NumericSeparatorClause = "score = 10e-1 /* retained NumericSeparatorClause */";
    internal const string NumericRadixSeparatorClause = "score = 1 /* retained NumericRadixSeparatorClause */";
    internal const string NumericOctalSeparatorClause = "score = 1 /* retained NumericOctalSeparatorClause */";
    internal const string Profile = "TSQL.NullDistinct";
    internal const string NullSynonymProfile = "SQL.NullPredicateSynonyms";
    internal const string NullClause = "score IS NULL /* retained NullClause */";
    internal const string NotNullClause = "score IS NOT NULL /* retained NotNullClause */";
    internal const string PositiveProfile = "SQL.PositiveNumericLiteral";
    internal const string PositiveClause = "score = +1";
    internal const string LeadingProfile = "SQL.LeadingDecimalLiteral";
    internal const string LeadingClause = "score > .5";
    internal const string HexProfile = "SQL.HexadecimalIntegerLiteral";
    internal const string HexClause = "score = 1 /* retained HexClause */";
    internal const string UnicodeProfile = "SQL.UnicodeStringLiteral";
    internal const string UnicodeClause = "score = 1 AND label = N'low' /* retained UnicodeClause */";
    internal const string UnicodeCustomClause = "score = 1 AND label = N'low' /* retained UnicodeCustomClause */";
    internal const string BinaryOctalProfile = "SQL.BinaryOctalIntegerLiteral";
    internal const string BinaryClause = "score = 1 /* retained BinaryClause */";
    internal const string OctalClause = "score = 1 /* retained OctalClause */";
    internal const string EscapeProfile = "SQL.EscapeStringLiteral";
    internal const string DollarProfile = "SQL.DollarQuotedString";
    internal const string DollarClause = "score = 1 AND label = N'low' /* retained DollarClause */";
    internal const string EscapeClause = "score = 1 AND label = N'low' /* retained EscapeClause */";
    internal const string ContinuationClause = "score = 1 AND label = N'low' /* retained ContinuationClause */";
    private const decimal Half = 0.5m;
    internal const string Prefix = "SELECT TOP (5) label AS label FROM null_ordering WHERE ";
    internal const string WindowPrefix = "SELECT label AS label FROM null_ordering WHERE ";
    internal const string Tail = " ORDER BY label ASC";
    internal static readonly FieldOperand Score = new("/score");
    internal static readonly FieldOperand Label = new("/label");
    internal static IReadOnlyList<SqlNullDistinctCase> All
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
        new("NOT (score IS NULL) /* retained literal 11 */", new Negation(new NullTest(Score, false, false)), ["high", "low", "missing"]),
        new("NOT (score IS NOT NULL) /* retained literal 12 */", new Negation(new NullTest(Score, true, false)), ["null_a", "null_b"]),
        new("score IS NULL OR label = 'missing' /* retained literal 13 */", Or(new NullTest(Score, false, false),
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
        new("score > 1 /* retained literal 28 */", new Comparison(Score, ">", ValueOperand.Create(1)), ["high"]),
        new("score = +1 /* retained literal 29 */", new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new("score = -1 /* retained literal 30 */", new Comparison(Score, "=", ValueOperand.Create(-1)), []),
        new("score BETWEEN 1 AND 2 /* retained literal 31 */", And(new Comparison(Score, ">=", ValueOperand.Create(1)),
            new Comparison(Score, "<=", ValueOperand.Create(2))), ["high", "low"]),
        new("score IN (1,2) /* retained literal 32 */", new InPredicate(Score, [ValueOperand.Create(1), ValueOperand.Create(2)], false), ["high", "low"]),
        new("score IS NOT DISTINCT FROM 1 /* retained literal 33 */", new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new("score = 79228162514264337593543950335 /* retained literal 34 */", new Comparison(Score, "=", ValueOperand.Create(decimal.MaxValue)), []),
        new("label = '0x1'", new Comparison(Label, "=", ValueOperand.Create("0x1")), []),
        new(EscapeClause, And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(Label, "=", ValueOperand.Create("low"))), ["low"]),
        new("label = N'low' /* retained literal 36 */", new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new("label = N'low' /* retained literal 37 */", new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new("label = N'low' /* retained literal 38 */", new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new("score = 1 AND N'😀' = N'😀' /* retained literal 39 */", And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create("😀"), "=", ValueOperand.Create("😀"))), ["low"]),
        new("score = 1 AND N'😀' = N'😀' /* retained literal 40 */", And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create("😀"), "=", ValueOperand.Create("😀"))), ["low"]),
        new("score = 1 AND N'😀' = N'😀' /* retained literal 41 */", And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create("😀"), "=", ValueOperand.Create("😀"))), ["low"]),
        new("label = N'''$1' /* retained literal 42 */", new Comparison(Label, "=", ValueOperand.Create("'$1")), []),
        new("label = N''' OR score = 1 --' /* retained literal 43 */", new Comparison(Label, "=", ValueOperand.Create("' OR score = 1 --")), []),
        new("score = 1 AND N'\b\f\n\r\t\\' = N'\b\f\n\r\t\\' /* retained literal 44 */",
            And(new Comparison(Score, "=", ValueOperand.Create(1)),
                new Comparison(ValueOperand.Create("\b\f\n\r\t\\"), "=", ValueOperand.Create("\b\f\n\r\t\\"))), ["low"]),
        new("label = N'low' /* retained literal 45 */", new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new("score = 1 AND N'x' = N'x' /* retained literal 46 */", And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create("x"), "=", ValueOperand.Create("x"))), ["low"]),
        new("score = 1 AND N'😀' = N'😀' /* retained literal 47 */", And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create("😀"), "=", ValueOperand.Create("😀"))), ["low"]),
        new("score = 1 AND N'😀' = N'😀' /* retained literal 48 */", And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create("😀"), "=", ValueOperand.Create("😀"))), ["low"]),
        new("label = N'low' /* retained literal 49 */", new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new("label = N'low' /* retained literal 50 */", new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new("label = N'low' /* retained literal 51 */", new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new("label = N'low' /* retained literal 52 */", new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new("label = N'low' /* retained literal 53 */", new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new(ContinuationClause, And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(Label, "=", ValueOperand.Create("low"))), ["low"]),
        new("score = 1 AND N'é' = N'é' /* retained literal 54 */", And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create("é"), "=", ValueOperand.Create("é"))), ["low"]),
        new("score = 1 AND N'😀' = N'😀' /* retained literal 55 */", And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create("😀"), "=", ValueOperand.Create("😀"))), ["low"]),
        new(DollarClause, And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(Label, "=", ValueOperand.Create("low"))), ["low"]),
        new("label = N'low' /* retained literal 56 */", new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new("label = N'low' /* retained literal 57 */", new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new("label = N'low' /* retained literal 58 */", new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new("label = N'' /* retained literal 59 */", new Comparison(Label, "=", ValueOperand.Create("")), []),
        new("label = N'l\\ow' /* retained literal 60 */", new Comparison(Label, "=", ValueOperand.Create("l\\ow")), []),
        new("label = N''' OR score = 1 --' /* retained literal 61 */", new Comparison(Label, "=", ValueOperand.Create("' OR score = 1 --")), []),
        new("label = N'$inner$low$inner$' /* retained literal 62 */", new Comparison(Label, "=", ValueOperand.Create("$inner$low$inner$")), []),
        new("score = 1 AND N'😀' = N'😀' /* retained literal 63 */", And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create("😀"), "=", ValueOperand.Create("😀"))), ["low"]),
        new("score = 1 /* retained BinaryClause */", new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new("score = +1 /* retained literal 65 */", new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new("score = -1 /* retained literal 66 */", new Comparison(Score, "=", ValueOperand.Create(-1)), []),
        new("score > 1 /* retained literal 67 */", new Comparison(Score, ">", ValueOperand.Create(1)), ["high"]),
        new("score = 1 /* retained OctalClause */", new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new("score = +1 /* retained literal 69 */", new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new("score = -1 /* retained literal 70 */", new Comparison(Score, "=", ValueOperand.Create(-1)), []),
        new("score > 1 /* retained literal 71 */", new Comparison(Score, ">", ValueOperand.Create(1)), ["high"]),
        new("score = 79228162514264337593543950335 /* retained literal 72 */", new Comparison(Score, "=", ValueOperand.Create(decimal.MaxValue)), []),
        new("score = 79228162514264337593543950335 /* retained literal 73 */", new Comparison(Score, "=", ValueOperand.Create(decimal.MaxValue)), []),
        new("score BETWEEN 1 AND 2 /* retained literal 74 */", And(new Comparison(Score, ">=", ValueOperand.Create(1)),
            new Comparison(Score, "<=", ValueOperand.Create(2))), ["high", "low"]),
        new("score IN (1,2) /* retained literal 75 */", new InPredicate(Score, [ValueOperand.Create(1), ValueOperand.Create(2)], false), ["high", "low"]),
        new("score IS NOT DISTINCT FROM 1 /* retained literal 76 */", new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new("score = 1 AND 79228162514264337593543950335 = 79228162514264337593543950335 /* retained literal 77 */", And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create(decimal.MaxValue), "=", ValueOperand.Create(decimal.MaxValue))), ["low"]),
        new("score = 1 AND 79228162514264337593543950335 = 79228162514264337593543950335 /* retained literal 78 */", And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create(decimal.MaxValue), "=", ValueOperand.Create(decimal.MaxValue))), ["low"]),
        new("score = 1 AND -79228162514264337593543950335 = -79228162514264337593543950335 /* retained literal 79 */", And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create(decimal.MinValue), "=", ValueOperand.Create(decimal.MinValue))), ["low"]),
        new("score = 1 AND -79228162514264337593543950335 = -79228162514264337593543950335 /* retained literal 80 */", And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create(decimal.MinValue), "=", ValueOperand.Create(decimal.MinValue))), ["low"]),
        new("score = 1 AND label = N'low' /* retained UnicodeClause */", And(new Comparison(Score, "=", ValueOperand.Create(1)), new Comparison(Label, "=", ValueOperand.Create("low"))), ["low"]),
        new("score = 1 AND label = N'low' /* retained UnicodeCustomClause */", And(new Comparison(Score, "=", ValueOperand.Create(1)), new Comparison(Label, "=", ValueOperand.Create("low"))), ["low"]),
        new("label = N'low' /* retained literal 83 */", new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new("label = N'low' /* retained literal 84 */", new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new("label = N'low' /* retained literal 85 */", new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new("label = N'low' /* retained literal 86 */", new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new("score = 1 AND N'😀' = N'😀' /* retained literal 87 */", And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create("😀"), "=", ValueOperand.Create("😀"))), ["low"]),
        new("score = 1 AND N'😀' = N'😀' /* retained literal 88 */", And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create("😀"), "=", ValueOperand.Create("😀"))), ["low"]),
        new("score = 1 AND N'😀' = N'😀' /* retained literal 89 */", And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create("😀"), "=", ValueOperand.Create("😀"))), ["low"]),
        new("label = N'\\' /* retained literal 90 */", new Comparison(Label, "=", ValueOperand.Create("\\")), []),
        new("label = N'low''other' /* retained literal 91 */", new Comparison(Label, "=", ValueOperand.Create("low'other")), []),
        new("score = 10e-1 /* retained NumericSeparatorClause */", new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new("score = .01e+02 /* retained literal 93 */", new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new("score = 10.00e-01 /* retained literal 94 */", new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new("score = +1 /* retained literal 95 */", new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new("score = 1 /* retained NumericRadixSeparatorClause */", new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new("score = 1 /* retained NumericOctalSeparatorClause */", new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new("score = -1 /* retained literal 98 */", new Comparison(Score, "=", ValueOperand.Create(-1)), []),
        new("score = 16 /* retained literal 99 */", new Comparison(Score, "=", ValueOperand.Create(16)), []),
        new("score = 2 /* retained literal 100 */", new Comparison(Score, "=", ValueOperand.Create(2)), ["high"]),
        new("score = 8 /* retained literal 101 */", new Comparison(Score, "=", ValueOperand.Create(8)), []),
        new("score = 79228162514264337593543950335 /* retained literal 102 */", new Comparison(Score, "=", ValueOperand.Create(decimal.MaxValue)), []),
        new("score = -79228162514264337593543950335 /* retained literal 103 */", new Comparison(Score, "=", ValueOperand.Create(decimal.MinValue)), []),
        new("score = 79228162514264337593543950335 /* retained literal 104 */", new Comparison(Score, "=", ValueOperand.Create(decimal.MaxValue)), []),
        new("score = 79228162514264337593543950335 /* retained literal 105 */", new Comparison(Score, "=", ValueOperand.Create(decimal.MaxValue)), []),
        new("score = 79228162514264337593543950335 /* retained literal 106 */", new Comparison(Score, "=", ValueOperand.Create(decimal.MaxValue)), []),
        .. SqlUnicodeIdentifierCases.All,
        .. SqlNullDistinctNullCases.All,
    ];
    internal static Logical And(Predicate left, Predicate right) => new(left, "AND", right);
    internal static Logical Or(Predicate left, Predicate right) => new(left, "OR", right);
    internal static AstQueryRequest Ast(PartitionRef partition, SqlNullDistinctCase item)
        => new(partition, new(SqlNullOrderingProtocol.Collection, null, [new("/label", "label")],
            item.Predicate, [new("/label", false)], SqlNullOrderingProtocol.ResultCount), AllowFullScan: true);
}
