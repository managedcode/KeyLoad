using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlBetweenGrammarTests
{
    private const string BetweenSql = "SELECT * FROM orders WHERE number BETWEEN 1 AND 9";
    private const string NotBetweenSql = "SELECT * FROM orders WHERE number NOT BETWEEN 1 AND 9";
    private const string MixedPrecedenceSql = "SELECT * FROM orders WHERE number BETWEEN 1 AND 5 OR flag = TRUE AND label = 'moss'";
    private const string AliasedQuotedSql = "SELECT * FROM orders AS o WHERE o.\"odd.name\" /* field */ BETWEEN /* bounds */ @lower AND @upper";

    [Test]
    public async Task AcSqlc006ABetweenLowersToExistingInclusiveComparisonsAgainstIndependentAst()
    {
        var actual = new SqlParser(BetweenSql, UnitExecutionOptions.DatabaseLimits(new()), UnitExecutionOptions.QueryExecution()).Parse().Filter;
        var operand = new FieldOperand(SqlBetweenTestData.NumberPath);
        var expected = new Logical(
            new Comparison(operand, ">=", ValueOperand.Create(1m)),
            "AND",
            new Comparison(operand, "<=", ValueOperand.Create(9m)));

        await Assert.That(actual).IsTypeOf<Logical>();
        await Assert.That(SamePredicate(actual!, expected)).IsTrue();
    }

    [Test]
    public async Task AcSqlc006ANotBetweenNegatesTheCompleteConjunction()
    {
        var actual = new SqlParser(NotBetweenSql, UnitExecutionOptions.DatabaseLimits(new()), UnitExecutionOptions.QueryExecution()).Parse().Filter;
        var inner = new Logical(
            new Comparison(new FieldOperand(SqlBetweenTestData.NumberPath), ">=", ValueOperand.Create(1m)),
            "AND",
            new Comparison(new FieldOperand(SqlBetweenTestData.NumberPath), "<=", ValueOperand.Create(9m)));

        await Assert.That(actual).IsTypeOf<Negation>();
        await Assert.That(SamePredicate(actual!, new Negation(inner))).IsTrue();
    }

    [Test]
    public async Task AcSqlc006ABetweenRetainsExistingAndBeforeOrPrecedence()
    {
        var actual = (Logical)new SqlParser(MixedPrecedenceSql, UnitExecutionOptions.DatabaseLimits(new()), UnitExecutionOptions.QueryExecution()).Parse().Filter!;
        var numberRange = Range(new FieldOperand(SqlBetweenTestData.NumberPath), ValueOperand.Create(1m), ValueOperand.Create(5m));
        var right = (Logical)actual.Right;

        await Assert.That(actual.Operator).IsEqualTo("OR");
        await Assert.That(SamePredicate(actual.Left, numberRange)).IsTrue();
        await Assert.That(right.Operator).IsEqualTo("AND");
        await Assert.That(((Comparison)right.Left).Left).IsEqualTo(new FieldOperand(SqlBetweenTestData.FlagPath));
        await Assert.That(((Comparison)right.Right).Left).IsEqualTo(new FieldOperand(SqlBetweenTestData.LabelPath));
    }

    [Test]
    public async Task AcSqlc006ABetweenUsesExistingAliasQuotedPathAndParameterOperands()
    {
        var query = new SqlParser(AliasedQuotedSql, UnitExecutionOptions.DatabaseLimits(new()), UnitExecutionOptions.QueryExecution()).Parse();
        var logical = (Logical)query.Filter!;

        await Assert.That(query.Collection).IsEqualTo(SqlBetweenTestData.Collection);
        await Assert.That(query.Alias).IsEqualTo("o");
        await Assert.That(((FieldOperand)((Comparison)logical.Left).Left).Path).IsEqualTo("/odd.name");
        await Assert.That(((ParameterOperand)((Comparison)logical.Left).Right).Name).IsEqualTo(SqlBetweenTestData.ParameterLow);
        await Assert.That(((ParameterOperand)((Comparison)logical.Right).Right).Name).IsEqualTo(SqlBetweenTestData.ParameterHigh);
    }

    [Test]
    public async Task AcSqlc006APrefixNotAndImmediateOuterAndKeepPredicateGrouping()
    {
        var prefixNot = new SqlParser("SELECT * FROM orders WHERE NOT number BETWEEN 1 AND 9", UnitExecutionOptions.DatabaseLimits(new()), UnitExecutionOptions.QueryExecution()).Parse().Filter;
        var immediateAnd = (Logical)new SqlParser("SELECT * FROM orders WHERE number BETWEEN 1 AND 5 AND flag = TRUE", UnitExecutionOptions.DatabaseLimits(new()), UnitExecutionOptions.QueryExecution()).Parse().Filter!;
        var range = Range(new FieldOperand(SqlBetweenTestData.NumberPath), ValueOperand.Create(1m), ValueOperand.Create(9m));
        var immediateRange = Range(new FieldOperand(SqlBetweenTestData.NumberPath), ValueOperand.Create(1m), ValueOperand.Create(5m));

        await Assert.That(SamePredicate(prefixNot!, new Negation(range))).IsTrue();
        await Assert.That(immediateAnd.Operator).IsEqualTo("AND");
        await Assert.That(SamePredicate(immediateAnd.Left, immediateRange)).IsTrue();
        await Assert.That(((Comparison)immediateAnd.Right).Left).IsEqualTo(new FieldOperand(SqlBetweenTestData.FlagPath));
    }

    private static Logical Range(Operand value, Operand lower, Operand upper)
        => new(new Comparison(value, ">=", lower), "AND", new Comparison(value, "<=", upper));

    private static bool SamePredicate(Predicate actual, Predicate expected)
        => JsonDefaults.Serialize<Predicate>(actual).SequenceEqual(JsonDefaults.Serialize<Predicate>(expected));
}
