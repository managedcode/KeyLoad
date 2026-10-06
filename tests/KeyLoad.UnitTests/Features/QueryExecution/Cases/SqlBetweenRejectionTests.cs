using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlBetweenRejectionTests
{
    private const string QuotedBetween = "SELECT * FROM orders WHERE number \"BETWEEN\" 1 AND 9";
    private const string QuotedDelimiter = "SELECT * FROM orders WHERE number BETWEEN 1 \"AND\" 9";
    private const string MissingLower = "SELECT * FROM orders WHERE number BETWEEN AND 9";
    private const string MissingDelimiter = "SELECT * FROM orders WHERE number BETWEEN 1 9";
    private const string MissingUpper = "SELECT * FROM orders WHERE number BETWEEN 1 AND";
    private const string RepeatedNot = "SELECT * FROM orders WHERE number NOT NOT BETWEEN 1 AND 9";
    private const string Symmetric = "SELECT * FROM orders WHERE number BETWEEN SYMMETRIC 1 AND 9";
    private const string Arithmetic = "SELECT * FROM orders WHERE number BETWEEN 1 + 2 AND 9";
    private const string SecondStatement = "SELECT * FROM orders WHERE number BETWEEN 1 AND 9; DELETE FROM orders";

    [Test]
    public async Task AcSqlc006AQuotedWordsCannotBecomeBetweenOrDelimiterSyntax()
    {
        await RejectValidation(QuotedBetween);
        await RejectValidation(QuotedDelimiter);
    }

    [Test]
    public async Task AcSqlc006AMalformedAndUnsupportedRangeExtensionsKeepRejections()
    {
        await RejectValidation(MissingLower);
        await RejectValidation(MissingDelimiter);
        await RejectValidation(MissingUpper);
        await RejectValidation(RepeatedNot);
        await RejectValidation(Symmetric);
        await RejectValidation(Arithmetic);
        var second = Assert.ThrowsExactly<KeyLoadException>(() => new SqlParser(SecondStatement, UnitExecutionOptions.DatabaseLimits(new()), UnitExecutionOptions.QueryExecution()).Parse());
        await Assert.That(second.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
    }

    private static async Task RejectValidation(string sql)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => new SqlParser(sql, UnitExecutionOptions.DatabaseLimits(new()), UnitExecutionOptions.QueryExecution()).Parse());
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
    }
}
