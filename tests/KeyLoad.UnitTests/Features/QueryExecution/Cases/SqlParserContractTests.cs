using System.Text;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlParserContractTests
{
    private const string QuotedSql = "SELECT o.\"odd.name\" AS \"odd.alias\", o.\"odd\"\"name\" AS \"odd\"\"alias\" FROM \"orders\" o "
        + "WHERE o.status = 'it''s' ORDER BY o.id DESC LIMIT 2";
    private const string PrecedenceSql = "SELECT * FROM orders WHERE NOT status = 'x' OR "
        + "score >= 2 AND id IN ('a',@wanted) ORDER BY id LIMIT 3";
    private const string SmallSql = "SELECT * FROM orders";

    [Test]
    public async Task AcRoc006_QuotedIdentifiersAliasesAndEscapedStringsKeepExactAst()
    {
        var query = new SqlParser(QuotedSql, UnitExecutionOptions.DatabaseLimits(new()), UnitExecutionOptions.QueryExecution()).Parse();
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
        var query = new SqlParser(PrecedenceSql, UnitExecutionOptions.DatabaseLimits(new()), UnitExecutionOptions.QueryExecution()).Parse();
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
        var query = new SqlParser("SELECT * FROM orders WHERE n IS NOT NULL AND n IS MISSING OR id NOT IN ('a')", UnitExecutionOptions.DatabaseLimits(new()), UnitExecutionOptions.QueryExecution()).Parse();
        var outer = (Logical)query.Filter!;
        var left = (Logical)outer.Left;
        await Assert.That(outer.Operator).IsEqualTo("OR");
        await Assert.That(((NullTest)left.Left).Negated).IsTrue();
        await Assert.That(((NullTest)left.Left).Missing).IsFalse();
        await Assert.That(((NullTest)left.Right).Missing).IsTrue();
        await Assert.That(((InPredicate)outer.Right).Negated).IsTrue();
    }

    [Test]
    public async Task AcRoc006_ExactTokenAndByteLimitsAndErrorPrecedence()
    {
        var bytes = Encoding.UTF8.GetByteCount(SmallSql);
        var exact = new DatabaseLimits { MaxQueryBytes = bytes, MaxQueryTokens = 4 };
        await Assert.That(new SqlParser(SmallSql, UnitExecutionOptions.DatabaseLimits(exact), UnitExecutionOptions.QueryExecution()).Parse().Collection).IsEqualTo("orders");
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => _ = new SqlParser(SmallSql, UnitExecutionOptions.DatabaseLimits(exact with
        {
            MaxQueryBytes = bytes - 1
        }), UnitExecutionOptions.QueryExecution())).Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => _ = new SqlParser(SmallSql, UnitExecutionOptions.DatabaseLimits(exact with
        {
            MaxQueryTokens = 3
        }), UnitExecutionOptions.QueryExecution())).Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => _ = new SqlParser("SELECT \"unterminated FROM orders", UnitExecutionOptions.DatabaseLimits(new()), UnitExecutionOptions.QueryExecution())).Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => new SqlParser("SELECT * FROM orders LIMIT 0 DESC", UnitExecutionOptions.DatabaseLimits(new()), UnitExecutionOptions.QueryExecution()).Parse()).Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => new SqlParser("SELECT * FROM orders WHERE (((status = 'x')))", UnitExecutionOptions.DatabaseLimits(new() { MaxQueryDepth = 2 }), UnitExecutionOptions.QueryExecution()).Parse()).Code)
            .IsEqualTo(ErrorCode.BudgetExceeded);
    }
}
