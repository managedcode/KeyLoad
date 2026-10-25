
namespace KeyLoad.UnitTests.RelationalStorage;

internal sealed class RelationalResourceTests
{
    private const string Alpha = "alpha";
    private const string EmptyKey = "\"key\":\"first\",";
    private const string NullableIndex = "nullable-name";
    private const string NullablePath = "/note";
    private const string NullNote = ",\"note\":null}";
    private const string Beta = "beta";

    [Test]
    public async Task AcAisql003RequiredPrimaryColumnCannotBeAbsent()
    {
        using var database = new TestDatabase();
        RelationalTestData.Configure(database);
        var row = RelationalTestData.Row().Replace(EmptyKey, string.Empty, StringComparison.Ordinal);
        await Assert.That(Errors.Code(RelationalTestData.Submit(database, new PutDocument(RelationalTestData.Table, RelationalTestData.First, row)).Problem))
            .IsEqualTo(ErrorCode.Validation);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcAisql004NullableUniquePolicyRemainsExplicit(bool includeNull)
    {
        using var database = new TestDatabase();
        var definition = RelationalTestData.Definition() with
        {
            Indexes = [new(NullableIndex, [NullablePath], Unique: true, IncludeNull: includeNull, IncludeMissing: false)]
        };
        RelationalTestData.Configure(database, definition);
        var first = RelationalTestData.Row()[..^1] + NullNote;
        var second = RelationalTestData.Row(RelationalTestData.Second).Replace(Alpha, Beta, StringComparison.Ordinal);
        second = second[..^1] + NullNote;
        database.Commit(new PutDocument(RelationalTestData.Table, RelationalTestData.First, first));
        var result = RelationalTestData.Submit(database, new PutDocument(RelationalTestData.Table, RelationalTestData.Second, second));
        await Assert.That(Errors.Code(result.Problem)).IsEqualTo(includeNull ? ErrorCode.Conflict : (ErrorCode?)null);
    }
}
