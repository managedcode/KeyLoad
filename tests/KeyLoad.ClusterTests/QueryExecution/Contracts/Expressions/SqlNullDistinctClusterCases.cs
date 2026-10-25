using KeyLoad.Query;

namespace KeyLoad.ClusterTests.QueryExecution;

internal static class SqlNullDistinctClusterCases
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
    internal static IReadOnlyList<SqlNullDistinctClusterCase> All
    {
        get;
    } =
    [
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score IS DISTINCT FROM NULL" + SqlNullDistinctClusterCases.Tail), And(new NullTest(Score, true, false), new NullTest(Score, true, true)), ["high", "low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score IS NOT DISTINCT FROM NULL" + SqlNullDistinctClusterCases.Tail), new NullTest(Score, false, false), ["null_a", "null_b"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score IS DISTINCT FROM 1" + SqlNullDistinctClusterCases.Tail), Or(new NullTest(Score, false, false), new Comparison(Score, "<>", ValueOperand.Create(1))), ["high", "null_a", "null_b"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score IS NOT DISTINCT FROM 1" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score IS DISTINCT FROM score" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "<>", Score), []),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score IS NOT DISTINCT FROM score" + SqlNullDistinctClusterCases.Tail), new NullTest(Score, true, true), ["high", "low", "null_a", "null_b"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "NOT (score IS DISTINCT FROM NULL)" + SqlNullDistinctClusterCases.Tail), new NullTest(Score, false, false), ["null_a", "null_b"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score IS DISTINCT FROM 1 OR label = 'missing'" + SqlNullDistinctClusterCases.Tail), Or(Or(new NullTest(Score, false, false),
            new Comparison(Score, "<>", ValueOperand.Create(1))), new Comparison(Label, "=", ValueOperand.Create("missing"))),
            ["high", "missing", "null_a", "null_b"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score IS NULL" + SqlNullDistinctClusterCases.Tail), new NullTest(Score, false, false), ["null_a", "null_b"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + NullClause + SqlNullDistinctClusterCases.Tail), new NullTest(Score, false, false), ["null_a", "null_b"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score IS NOT NULL" + SqlNullDistinctClusterCases.Tail), new NullTest(Score, true, false), ["high", "low", "missing"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + NotNullClause + SqlNullDistinctClusterCases.Tail), new NullTest(Score, true, false), ["high", "low", "missing"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "NOT (score IS NULL) /* retained literal 11 */" + SqlNullDistinctClusterCases.Tail), new Negation(new NullTest(Score, false, false)), ["high", "low", "missing"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "NOT (score IS NOT NULL) /* retained literal 12 */" + SqlNullDistinctClusterCases.Tail), new Negation(new NullTest(Score, true, false)), ["null_a", "null_b"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score IS NULL OR label = 'missing' /* retained literal 13 */" + SqlNullDistinctClusterCases.Tail), Or(new NullTest(Score, false, false),
            new Comparison(Label, "=", ValueOperand.Create("missing"))), ["missing", "null_a", "null_b"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + PositiveClause + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score > +1" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, ">", ValueOperand.Create(1)), ["high"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score BETWEEN +1 AND +2" + SqlNullDistinctClusterCases.Tail), And(new Comparison(Score, ">=", ValueOperand.Create(1)),
            new Comparison(Score, "<=", ValueOperand.Create(2))), ["high", "low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score IN (+1,+2)" + SqlNullDistinctClusterCases.Tail), new InPredicate(Score, [ValueOperand.Create(1), ValueOperand.Create(2)], false), ["high", "low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score IS NOT DISTINCT FROM +1" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = +1e+0" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = -1" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(-1)), []),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = +79228162514264337593543950335" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=",
            ValueOperand.Create(decimal.MaxValue)), []),
        new(() => new(SqlNullDistinctClusterCases.Prefix + LeadingClause + SqlNullDistinctClusterCases.Tail), new Comparison(Score, ">", ValueOperand.Create(Half)), ["high", "low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = .1e1" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score > -.5" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, ">", ValueOperand.Create(-Half)), ["high", "low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score > +.5" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, ">", ValueOperand.Create(Half)), ["high", "low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score BETWEEN .5 AND 1." + SqlNullDistinctClusterCases.Tail), And(new Comparison(Score, ">=", ValueOperand.Create(Half)),
            new Comparison(Score, "<=", ValueOperand.Create(1))), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score IN (.5,1.)" + SqlNullDistinctClusterCases.Tail), new InPredicate(Score, [ValueOperand.Create(Half), ValueOperand.Create(1)], false), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = .5" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(Half)), []),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score IS NOT DISTINCT FROM .1e1" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + HexClause + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score > 1 /* retained literal 28 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, ">", ValueOperand.Create(1)), ["high"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = +1 /* retained literal 29 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = -1 /* retained literal 30 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(-1)), []),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score BETWEEN 1 AND 2 /* retained literal 31 */" + SqlNullDistinctClusterCases.Tail), And(new Comparison(Score, ">=", ValueOperand.Create(1)),
            new Comparison(Score, "<=", ValueOperand.Create(2))), ["high", "low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score IN (1,2) /* retained literal 32 */" + SqlNullDistinctClusterCases.Tail), new InPredicate(Score, [ValueOperand.Create(1), ValueOperand.Create(2)], false), ["high", "low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score IS NOT DISTINCT FROM 1 /* retained literal 33 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = 79228162514264337593543950335 /* retained literal 34 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(decimal.MaxValue)), []),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "label = '0x1'" + SqlNullDistinctClusterCases.Tail), new Comparison(Label, "=", ValueOperand.Create("0x1")), []),
        new(() => new(SqlNullDistinctClusterCases.Prefix + EscapeClause + SqlNullDistinctClusterCases.Tail), And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(Label, "=", ValueOperand.Create("low"))), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "label = N'low' /* retained literal 36 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "label = N'low' /* retained literal 37 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "label = N'low' /* retained literal 38 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = 1 AND N'😀' = N'😀' /* retained literal 39 */" + SqlNullDistinctClusterCases.Tail), And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create("😀"), "=", ValueOperand.Create("😀"))), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = 1 AND N'😀' = N'😀' /* retained literal 40 */" + SqlNullDistinctClusterCases.Tail), And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create("😀"), "=", ValueOperand.Create("😀"))), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = 1 AND N'😀' = N'😀' /* retained literal 41 */" + SqlNullDistinctClusterCases.Tail), And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create("😀"), "=", ValueOperand.Create("😀"))), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "label = N'''$1' /* retained literal 42 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Label, "=", ValueOperand.Create("'$1")), []),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "label = N''' OR score = 1 --' /* retained literal 43 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Label, "=", ValueOperand.Create("' OR score = 1 --")), []),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = 1 AND N'\b\f\n\r\t\\' = N'\b\f\n\r\t\\' /* retained literal 44 */" + SqlNullDistinctClusterCases.Tail),
            And(new Comparison(Score, "=", ValueOperand.Create(1)),
                new Comparison(ValueOperand.Create("\b\f\n\r\t\\"), "=", ValueOperand.Create("\b\f\n\r\t\\"))), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "label = N'low' /* retained literal 45 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = 1 AND N'x' = N'x' /* retained literal 46 */" + SqlNullDistinctClusterCases.Tail), And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create("x"), "=", ValueOperand.Create("x"))), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = 1 AND N'😀' = N'😀' /* retained literal 47 */" + SqlNullDistinctClusterCases.Tail), And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create("😀"), "=", ValueOperand.Create("😀"))), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = 1 AND N'😀' = N'😀' /* retained literal 48 */" + SqlNullDistinctClusterCases.Tail), And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create("😀"), "=", ValueOperand.Create("😀"))), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "label = N'low' /* retained literal 49 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "label = N'low' /* retained literal 50 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "label = N'low' /* retained literal 51 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "label = N'low' /* retained literal 52 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "label = N'low' /* retained literal 53 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + ContinuationClause + SqlNullDistinctClusterCases.Tail), And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(Label, "=", ValueOperand.Create("low"))), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = 1 AND N'é' = N'é' /* retained literal 54 */" + SqlNullDistinctClusterCases.Tail), And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create("é"), "=", ValueOperand.Create("é"))), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = 1 AND N'😀' = N'😀' /* retained literal 55 */" + SqlNullDistinctClusterCases.Tail), And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create("😀"), "=", ValueOperand.Create("😀"))), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + DollarClause + SqlNullDistinctClusterCases.Tail), And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(Label, "=", ValueOperand.Create("low"))), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "label = N'low' /* retained literal 56 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "label = N'low' /* retained literal 57 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "label = N'low' /* retained literal 58 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "label = N'' /* retained literal 59 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Label, "=", ValueOperand.Create("")), []),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "label = N'l\\ow' /* retained literal 60 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Label, "=", ValueOperand.Create("l\\ow")), []),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "label = N''' OR score = 1 --' /* retained literal 61 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Label, "=", ValueOperand.Create("' OR score = 1 --")), []),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "label = N'$inner$low$inner$' /* retained literal 62 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Label, "=", ValueOperand.Create("$inner$low$inner$")), []),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = 1 AND N'😀' = N'😀' /* retained literal 63 */" + SqlNullDistinctClusterCases.Tail), And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create("😀"), "=", ValueOperand.Create("😀"))), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = 1 /* retained BinaryClause */" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = +1 /* retained literal 65 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = -1 /* retained literal 66 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(-1)), []),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score > 1 /* retained literal 67 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, ">", ValueOperand.Create(1)), ["high"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = 1 /* retained OctalClause */" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = +1 /* retained literal 69 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = -1 /* retained literal 70 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(-1)), []),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score > 1 /* retained literal 71 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, ">", ValueOperand.Create(1)), ["high"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = 79228162514264337593543950335 /* retained literal 72 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(decimal.MaxValue)), []),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = 79228162514264337593543950335 /* retained literal 73 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(decimal.MaxValue)), []),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score BETWEEN 1 AND 2 /* retained literal 74 */" + SqlNullDistinctClusterCases.Tail), And(new Comparison(Score, ">=", ValueOperand.Create(1)),
            new Comparison(Score, "<=", ValueOperand.Create(2))), ["high", "low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score IN (1,2) /* retained literal 75 */" + SqlNullDistinctClusterCases.Tail), new InPredicate(Score, [ValueOperand.Create(1), ValueOperand.Create(2)], false), ["high", "low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score IS NOT DISTINCT FROM 1 /* retained literal 76 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = 1 AND 79228162514264337593543950335 = 79228162514264337593543950335 /* retained literal 77 */" + SqlNullDistinctClusterCases.Tail), And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create(decimal.MaxValue), "=", ValueOperand.Create(decimal.MaxValue))), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = 1 AND 79228162514264337593543950335 = 79228162514264337593543950335 /* retained literal 78 */" + SqlNullDistinctClusterCases.Tail), And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create(decimal.MaxValue), "=", ValueOperand.Create(decimal.MaxValue))), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = 1 AND -79228162514264337593543950335 = -79228162514264337593543950335 /* retained literal 79 */" + SqlNullDistinctClusterCases.Tail), And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create(decimal.MinValue), "=", ValueOperand.Create(decimal.MinValue))), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = 1 AND -79228162514264337593543950335 = -79228162514264337593543950335 /* retained literal 80 */" + SqlNullDistinctClusterCases.Tail), And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create(decimal.MinValue), "=", ValueOperand.Create(decimal.MinValue))), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = 1 AND label = N'low' /* retained UnicodeClause */" + SqlNullDistinctClusterCases.Tail), And(new Comparison(Score, "=", ValueOperand.Create(1)), new Comparison(Label, "=", ValueOperand.Create("low"))), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = 1 AND label = N'low' /* retained UnicodeCustomClause */" + SqlNullDistinctClusterCases.Tail), And(new Comparison(Score, "=", ValueOperand.Create(1)), new Comparison(Label, "=", ValueOperand.Create("low"))), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "label = N'low' /* retained literal 83 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "label = N'low' /* retained literal 84 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "label = N'low' /* retained literal 85 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "label = N'low' /* retained literal 86 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Label, "=", ValueOperand.Create("low")), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = 1 AND N'😀' = N'😀' /* retained literal 87 */" + SqlNullDistinctClusterCases.Tail), And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create("😀"), "=", ValueOperand.Create("😀"))), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = 1 AND N'😀' = N'😀' /* retained literal 88 */" + SqlNullDistinctClusterCases.Tail), And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create("😀"), "=", ValueOperand.Create("😀"))), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = 1 AND N'😀' = N'😀' /* retained literal 89 */" + SqlNullDistinctClusterCases.Tail), And(new Comparison(Score, "=", ValueOperand.Create(1)),
            new Comparison(ValueOperand.Create("😀"), "=", ValueOperand.Create("😀"))), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "label = N'\\' /* retained literal 90 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Label, "=", ValueOperand.Create("\\")), []),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "label = N'low''other' /* retained literal 91 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Label, "=", ValueOperand.Create("low'other")), []),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = 10e-1 /* retained NumericSeparatorClause */" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = .01e+02 /* retained literal 93 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = 10.00e-01 /* retained literal 94 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = +1 /* retained literal 95 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = 1 /* retained NumericRadixSeparatorClause */" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = 1 /* retained NumericOctalSeparatorClause */" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(1)), ["low"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = -1 /* retained literal 98 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(-1)), []),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = 16 /* retained literal 99 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(16)), []),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = 2 /* retained literal 100 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(2)), ["high"]),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = 8 /* retained literal 101 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(8)), []),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = 79228162514264337593543950335 /* retained literal 102 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(decimal.MaxValue)), []),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = -79228162514264337593543950335 /* retained literal 103 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(decimal.MinValue)), []),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = 79228162514264337593543950335 /* retained literal 104 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(decimal.MaxValue)), []),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = 79228162514264337593543950335 /* retained literal 105 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(decimal.MaxValue)), []),
        new(() => new(SqlNullDistinctClusterCases.Prefix + "score = 79228162514264337593543950335 /* retained literal 106 */" + SqlNullDistinctClusterCases.Tail), new Comparison(Score, "=", ValueOperand.Create(decimal.MaxValue)), []),
        .. SqlUnicodeIdentifierCases.All,
        .. SqlNullDistinctClusterNullCases.All,
    ];
    internal static Logical And(Predicate left, Predicate right) => new(left, "AND", right);
    internal static Logical Or(Predicate left, Predicate right) => new(left, "OR", right);
    internal static AstQueryRequest Ast(PartitionRef partition, SqlNullDistinctClusterCase item)
        => new(partition, new(SqlNullOrderingClusterProtocol.Collection, null, [new("/label", "label")],
            item.Predicate, [new("/label", false)], SqlNullOrderingClusterProtocol.RowCount), AllowFullScan: true);
}
