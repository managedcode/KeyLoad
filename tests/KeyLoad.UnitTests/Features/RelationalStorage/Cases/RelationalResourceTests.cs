using System.Text;

namespace KeyLoad.UnitTests.Features.RelationalStorage;

internal sealed class RelationalResourceTests
{
    private const string Alpha = "alpha";
    private const string UnicodeValue = "𝄞𝄞𝄞𝄞";
    private const string EmptyKey = "\"key\":\"first\",";
    private const string NullableIndex = "nullable-name";
    private const string NullablePath = "/note";
    private const string NullNote = ",\"note\":null}";
    private const string Beta = "beta";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcAisql003OriginalUtf8ByteLimitRejectsBeforeRowPublication(bool unicode)
    {
        var row = unicode ? RelationalTestData.Row().Replace(Alpha, UnicodeValue, StringComparison.Ordinal) : RelationalTestData.Row();
        var byteLimit = unicode ? row.Length : Encoding.UTF8.GetByteCount(row) - 1;
        using var database = new TestDatabase(new() { MaxDocumentBytes = byteLimit });
        RelationalTestData.Configure(database);
        var rejected = RelationalTestData.Submit(database, new PutDocument(RelationalTestData.Table, RelationalTestData.First, row));
        await Assert.That(rejected.Error).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(database.Database.GetDocument(RelationalTestData.Root,
            new(database.Partition, RelationalTestData.Table, RelationalTestData.First))).IsNull();
    }

    [Test]
    public async Task AcAisql003RequiredPrimaryColumnCannotBeAbsent()
    {
        using var database = new TestDatabase();
        RelationalTestData.Configure(database);
        var row = RelationalTestData.Row().Replace(EmptyKey, string.Empty, StringComparison.Ordinal);
        await Assert.That(RelationalTestData.Submit(database, new PutDocument(RelationalTestData.Table, RelationalTestData.First, row)).Error)
            .IsEqualTo(ErrorCode.Validation);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcAisql004NativeNullableUniquePolicyRemainsExplicit(bool includeNull)
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
        await Assert.That(result.Error).IsEqualTo(includeNull ? ErrorCode.Conflict : (ErrorCode?)null);
    }
}
