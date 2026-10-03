using System.Text.Json;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlBetweenTruthTests
{
    private const string UnknownMiddle = "SELECT * FROM orders WHERE id = 'middle' AND number NOT BETWEEN NULL AND 8";
    private const string FalseUpper = "SELECT * FROM orders WHERE id = 'high' AND number NOT BETWEEN NULL AND 8";
    private const string FalseLower = "SELECT * FROM orders WHERE id = 'low' AND number NOT BETWEEN 2 AND NULL";
    private const string UnknownUpper = "SELECT * FROM orders WHERE id = 'middle' AND number NOT BETWEEN 2 AND NULL";
    private const string NullMismatch = "SELECT * FROM orders WHERE id = 'null-number' AND number NOT BETWEEN 'wrong-type' AND 8";
    private const string MissingMismatch = "SELECT * FROM orders WHERE id = 'missing-number' AND number BETWEEN 'wrong-type' AND 8";
    private const string MissingBetween = "SELECT * FROM orders WHERE id = 'missing-number' AND number BETWEEN 1 AND 9";
    private const string EagerMismatch = "SELECT * FROM orders WHERE id = 'low' AND number BETWEEN 2 AND 'wrong-type'";
    private const string NullBetween = "SELECT * FROM orders WHERE id = 'null-number' AND number BETWEEN 1 AND 9";
    private const string NullNotBetween = "SELECT * FROM orders WHERE id = 'null-number' AND number NOT BETWEEN 1 AND 9";
    private const string MissingNotBetween = "SELECT * FROM orders WHERE id = 'missing-number' AND number NOT BETWEEN 1 AND 9";
    private const string BothNullBounds = "SELECT * FROM orders WHERE id = 'middle' AND number BETWEEN NULL AND NULL";
    private const string BothNullBoundsNegated = "SELECT * FROM orders WHERE id = 'middle' AND number NOT BETWEEN NULL AND NULL";

    [Test]
    public async Task AcSqlc006AThreeValuedAndUsesFalseDominanceBeforeNot()
    {
        using var database = SqlBetweenTestData.Create();
        var engine = new QueryEngine(database.Database);

        var unknownAndTrue = engine.Execute(SqlBetweenTestData.Root, SqlBetweenTestData.Request(database, UnknownMiddle));
        var unknownAndFalse = engine.Execute(SqlBetweenTestData.Root, SqlBetweenTestData.Request(database, FalseUpper));
        var falseAndUnknown = engine.Execute(SqlBetweenTestData.Root, SqlBetweenTestData.Request(database, FalseLower));
        var trueAndUnknown = engine.Execute(SqlBetweenTestData.Root, SqlBetweenTestData.Request(database, UnknownUpper));

        await Assert.That(unknownAndTrue.Rows).IsEmpty();
        await Assert.That(SqlBetweenTestData.Ids(unknownAndFalse)).IsEqualTo(SqlBetweenTestData.RowHigh);
        await Assert.That(SqlBetweenTestData.Ids(falseAndUnknown)).IsEqualTo(SqlBetweenTestData.RowLow);
        await Assert.That(trueAndUnknown.Rows).IsEmpty();
    }

    [Test]
    public async Task AcSqlc006ANullAndMissingValuesRemainUnknownBeforeBoundTypeChecks()
    {
        using var database = SqlBetweenTestData.Create();
        var engine = new QueryEngine(database.Database);

        var explicitNull = engine.Execute(SqlBetweenTestData.Root, SqlBetweenTestData.Request(database, NullMismatch));
        var missing = engine.Execute(SqlBetweenTestData.Root, SqlBetweenTestData.Request(database, MissingMismatch));
        var nullBetween = engine.Execute(SqlBetweenTestData.Root, SqlBetweenTestData.Request(database, NullBetween));
        var nullNotBetween = engine.Execute(SqlBetweenTestData.Root, SqlBetweenTestData.Request(database, NullNotBetween));
        var missingBetween = engine.Execute(SqlBetweenTestData.Root, SqlBetweenTestData.Request(database, MissingBetween));
        var missingNotBetween = engine.Execute(SqlBetweenTestData.Root, SqlBetweenTestData.Request(database, MissingNotBetween));
        var bothNull = engine.Execute(SqlBetweenTestData.Root, SqlBetweenTestData.Request(database, BothNullBounds));
        var bothNullNegated = engine.Execute(SqlBetweenTestData.Root, SqlBetweenTestData.Request(database, BothNullBoundsNegated));

        await Assert.That(explicitNull.Rows).IsEmpty();
        await Assert.That(missing.Rows).IsEmpty();
        await Assert.That(nullBetween.Rows).IsEmpty();
        await Assert.That(nullNotBetween.Rows).IsEmpty();
        await Assert.That(missingBetween.Rows).IsEmpty();
        await Assert.That(missingNotBetween.Rows).IsEmpty();
        await Assert.That(bothNull.Rows).IsEmpty();
        await Assert.That(bothNullNegated.Rows).IsEmpty();
    }

    [Test]
    public async Task AcSqlc006ANonNullMismatchedBoundsFailEvenAfterOtherComparisonIsFalse()
    {
        using var database = SqlBetweenTestData.Create();
        var engine = new QueryEngine(database.Database);

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlBetweenTestData.Root,
            SqlBetweenTestData.Request(database, EagerMismatch)));
        var recovered = engine.Execute(SqlBetweenTestData.Root,
            SqlBetweenTestData.Request(database, "SELECT * FROM orders WHERE id = 'middle' AND number BETWEEN 1 AND 9"));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(SqlBetweenTestData.Ids(recovered)).IsEqualTo(SqlBetweenTestData.RowMiddle);
    }

    [Test]
    public async Task AcSqlc006AMissingParametersAndNonscalarOperandsKeepExistingErrors()
    {
        using var database = SqlBetweenTestData.Create();
        database.Commit(new PutDocument(SqlBetweenTestData.Collection, "object-number", "{\"number\":{\"nested\":1}}"));
        var engine = new QueryEngine(database.Database);
        var missingParameter = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlBetweenTestData.Root,
            SqlBetweenTestData.Request(database, "SELECT * FROM orders WHERE number BETWEEN @lower AND 9")));
        var nonscalar = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlBetweenTestData.Root,
            SqlBetweenTestData.Request(database, "SELECT * FROM orders WHERE id = 'object-number' AND number BETWEEN 1 AND 9")));
        var nonscalarParameter = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlBetweenTestData.Root,
            SqlBetweenTestData.Request(database, "SELECT * FROM orders WHERE number BETWEEN @lower AND 9",
                new() { [SqlBetweenTestData.ParameterLow] = JsonSerializer.SerializeToElement(new { nested = 1 }) })));

        await Assert.That(missingParameter.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(nonscalar.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(nonscalarParameter.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(SqlBetweenTestData.Ids(engine.Execute(SqlBetweenTestData.Root,
            SqlBetweenTestData.Request(database, "SELECT * FROM orders WHERE id = 'low' AND number BETWEEN 1 AND 9"))))
            .IsEqualTo(SqlBetweenTestData.RowLow);
    }
}
