
namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class EqualityAccessPathBudgetTests
{
    private const int ReadBudgetBytes = 8_192;
    private const int PayloadLength = 16_384;
    private const string PayloadPrefix = "{\"status\":\"open\",\"payload\":\"";
    private const string PayloadSuffix = "\"}";
    private const char PayloadCharacter = 'x';

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AcAisql008ReversedLookupChargesTheSameRawDocumentAndIndexReadBudget(bool indexed, bool parameterized)
    {
        using var db = new TestDatabase();
        db.Configure(EqualityAccessPathSupport.Collection, ResourceKind.Collection,
            indexes: [new(EqualityAccessPathSupport.StatusIndex, [EqualityAccessPathSupport.StatusPath])]);
        db.Commit(new PutDocument(EqualityAccessPathSupport.Collection, EqualityAccessPathSupport.PrimaryId,
            PayloadPrefix + new string(PayloadCharacter, PayloadLength) + PayloadSuffix));
        var engine = QueryBudgetReadOwner.Create(db, db.Database.Limits with { MaxQueryReadBytes = ReadBudgetBytes });
        await Assert.That(engine.Execute("root", new(db.Partition, "SELECT * FROM orders WHERE id = 'absent'")).Rows).IsEmpty();
        foreach (var reversed in new[] { false, true })
        {
            var request = EqualityAccessPathSupport.Request(db, indexed, parameterized, reversed);
            await Assert.That(EqualityAccessPathSupport.Failure(engine, request).Code).IsEqualTo(ErrorCode.BudgetExceeded);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcAisql008ReversedIndexCandidatesPreserveTheSharedLimitAndPointFollowup(bool parameterized)
    {
        using var db = EqualityAccessPathSupport.Create();
        var engine = QueryBudgetReadOwner.Create(db, db.Database.Limits with { MaxScanRecords = 1 });
        foreach (var reversed in new[] { false, true })
        {
            var request = EqualityAccessPathSupport.Request(db, indexed: true, parameterized, reversed);
            await Assert.That(EqualityAccessPathSupport.Failure(engine, request).Code).IsEqualTo(ErrorCode.BudgetExceeded);
        }
        var point = engine.Execute(EqualityAccessPathSupport.Root,
            EqualityAccessPathSupport.Request(db, indexed: false, parameterized, reversed: true));
        await Assert.That(point.Rows).HasSingleItem();
        await Assert.That(point.Rows[0].EntityId).IsEqualTo(EqualityAccessPathSupport.PrimaryId);
        await Assert.That(point.AccessPath).IsEqualTo(EqualityAccessPathSupport.PointPath);
    }
}
