using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed class SqlParserContractTests
{
    private const string QuotedSql = "SELECT TOP (2) o.\"odd.name\" AS \"odd.alias\", o.\"odd\"\"name\" AS \"odd\"\"alias\" FROM \"orders\" o "
        + "WHERE o.status = 'it''s' ORDER BY o.id DESC";
    private const string PrecedenceSql = "SELECT TOP (3) * FROM orders WHERE NOT status = 'x' OR "
        + "score >= 2 AND id IN ('a',@wanted) ORDER BY id";

    [Test]
    public async Task AcRoc006_QuotedIdentifiersAliasesAndEscapedStringsKeepExactAst()
    {
        var query = new SqlParser(QuotedSql, UnitExecutionOptions.QueryExecution()).Parse();
        var filter = (Comparison)query.Filter!;
        await Assert.That(query.Collection).IsEqualTo("orders");
        await Assert.That(query.Alias).IsEqualTo("o");
        await Assert.That(query.Projection[0].Path).IsEqualTo("/odd.name");
        await Assert.That(query.Projection[0].Alias).IsEqualTo("odd.alias");
        await Assert.That(query.Projection[1].Path).IsEqualTo("/odd\"name");
        await Assert.That(query.Projection[1].Alias).IsEqualTo("odd\"alias");
        await Assert.That(((FieldOperand)filter.Left).Path).IsEqualTo("/status");
        await Assert.That(((ValueOperand)filter.Right).Value.GetString()).IsEqualTo("it's");
        await Assert.That(query.Order[0].Path).IsEqualTo("/@id");
        await Assert.That(query.Order[0].Descending).IsTrue();
        await Assert.That(query.Limit).IsEqualTo(2);
    }

    [Test]
    public async Task AcRoc006_NotAndTakePrecedenceOverOrWithInParameters()
    {
        var query = new SqlParser(PrecedenceSql, UnitExecutionOptions.QueryExecution()).Parse();
        var outer = (Logical)query.Filter!;
        var right = (Logical)outer.Right;
        var values = (InPredicate)right.Right;
        await Assert.That(outer.Operator).IsEqualTo("OR");
        await Assert.That(((Negation)outer.Left).Inner).IsTypeOf<Comparison>();
        await Assert.That(right.Operator).IsEqualTo("AND");
        await Assert.That(values.Values.Length).IsEqualTo(2);
        await Assert.That(((ValueOperand)values.Values[0]).Value.GetString()).IsEqualTo("a");
        await Assert.That(((ParameterOperand)values.Values[1]).Name).IsEqualTo("wanted");
        await Assert.That(query.Order[0].Path).IsEqualTo("/@id");
        await Assert.That(query.Limit).IsEqualTo(3);
    }

    [Test]
    public async Task AcRoc006_NullMissingAndNegatedMembershipRetainShape()
    {
        var query = new SqlParser("SELECT * FROM orders WHERE n IS NOT NULL OR id NOT IN ('a')", UnitExecutionOptions.QueryExecution()).Parse();
        var outer = (Logical)query.Filter!;
        var left = (NullTest)outer.Left;
        await Assert.That(outer.Operator).IsEqualTo("OR");
        await Assert.That(left.Negated).IsTrue();
        await Assert.That(left.Missing).IsFalse();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => new SqlParser(
            "SELECT * FROM orders WHERE n IS MISSING",
            UnitExecutionOptions.QueryExecution()).Parse()).Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(((InPredicate)outer.Right).Negated).IsTrue();
    }

}
