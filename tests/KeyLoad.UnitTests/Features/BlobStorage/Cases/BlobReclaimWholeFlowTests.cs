using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed class BlobReclaimWholeFlowTests
{
    private const string CorruptionDetail = "The persisted blob records are inconsistent.";
    private const byte CorruptByte = 0xFE;

    [Test]
    public async Task AcBlob004AbortedPartialReclaimColdCursorThenReuseRejectsOldLifetimeAndPublishesHealthyRanges()
    {
        using var database = new TestDatabase();
        var fixture = await BlobReclaimWholeFlow.Start(database);
        var first = await BlobReclaimWholeFlow.First(database.Database, database.Store, fixture);
        var position = database.Store.Position;
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        database.Store.Dispose();
        using var reopened = new ZoneTreeStore(new(database.Directory), database.StorageExecution,
            UnitExecutionOptions.PointCacheExecution());
        var owner = database.CreateConfiguredDatabase(reopened);
        await BlobWholeFlowAssertions.Unchanged(reopened, position, image);
        await BlobReclaimWholeFlow.AssertState(owner, fixture);
        await BlobWholeFlowAssertions.Quotas(owner, fixture.Blob, BlobLimits.RawPartBytes,
            BlobReclaimWholeFlowFixture.SingleCount, BlobReclaimWholeFlowFixture.EmptyCount);
        await BlobWholeFlowAssertions.Parts(owner, fixture, false, true);
        await BlobReclaimWholeFlow.Replay(owner, reopened, OperationKind.ReclaimBlob,
            first.Request, first.Request.CommandId, first.Result);
        var final = await BlobReclaimWholeFlow.Finish(owner, reopened, fixture);
        await BlobReclaimHealthyContinuation.Run(owner, reopened, fixture, first.Request, final);
    }

    [Test]
    public async Task AcBlob004CorruptSecondPartReclaimRollsBackFirstDeletionThenRepairColdReclaimAndHealthyPublish()
    {
        using var database = new TestDatabase();
        var fixture = await BlobReclaimWholeFlow.Start(database);
        var secondKey = BlobKeys.Part(fixture.Blob, fixture.UploadId, BlobReclaimWholeFlowFixture.SecondOrdinal);
        var damaged = BlobReclaimWholeFlowFixture.Payload.AsSpan(BlobLimits.RawPartBytes).ToArray();
        damaged[BlobReclaimWholeFlowFixture.FirstOrdinal] = CorruptByte;
        database.Store.Commit((tx, _) => { tx.Put(secondKey, damaged); return true; });
        await RejectedReclaimPreservesEntireStore(database, fixture);
        database.Store.Commit((tx, _) =>
        { tx.Put(secondKey, BlobReclaimWholeFlowFixture.Payload.AsSpan(BlobLimits.RawPartBytes).ToArray()); return true; });
        await BlobWholeFlowAssertions.Parts(database.Database, fixture, true, true);
        var first = await BlobReclaimWholeFlow.First(database.Database, database.Store, fixture);
        var position = database.Store.Position;
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        database.Store.Dispose();
        using var reopened = new ZoneTreeStore(new(database.Directory), database.StorageExecution,
            UnitExecutionOptions.PointCacheExecution());
        var owner = database.CreateConfiguredDatabase(reopened);
        await BlobWholeFlowAssertions.Unchanged(reopened, position, image);
        await BlobReclaimWholeFlow.AssertState(owner, fixture);
        await BlobReclaimWholeFlow.Replay(owner, reopened, OperationKind.ReclaimBlob,
            first.Request, first.Request.CommandId, first.Result);
        var final = await BlobReclaimWholeFlow.Finish(owner, reopened, fixture);
        await BlobReclaimHealthyContinuation.Run(owner, reopened, fixture, first.Request, final);
    }

    private static async Task RejectedReclaimPreservesEntireStore(TestDatabase database, BlobReclaimWholeFlowFixture fixture)
    {
        var request = new ReclaimBlobRequest(Guid.NewGuid(), fixture.Blob, fixture.UploadId,
            BlobReclaimWholeFlowFixture.AcceptedParts);
        var position = database.Store.Position;
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        var originalState = new BlobStorageOperations(database.Database).UploadInfo(BlobReclaimWholeFlowFixture.Principal,
            new(fixture.Blob, fixture.UploadId));
        var first = Assert.ThrowsExactly<KeyLoadException>(() =>
            BlobReclaimWholeFlow.Apply(database.Database, OperationKind.ReclaimBlob, request, request.CommandId));
        await Assert.That(first.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(first.Message).IsEqualTo(CorruptionDetail);
        await BlobWholeFlowAssertions.Unchanged(database.Store, position, image);
        var repeated = Assert.ThrowsExactly<KeyLoadException>(() =>
            BlobReclaimWholeFlow.Apply(database.Database, OperationKind.ReclaimBlob, request, request.CommandId));
        await Assert.That(repeated.Code).IsEqualTo(first.Code);
        await Assert.That(repeated.Message).IsEqualTo(first.Message);
        await BlobWholeFlowAssertions.Unchanged(database.Store, position, image);
        await BlobWholeFlowAssertions.Exact(new BlobStorageOperations(database.Database).UploadInfo(
            BlobReclaimWholeFlowFixture.Principal, new(fixture.Blob, fixture.UploadId)), originalState);
        await BlobWholeFlowAssertions.Quotas(database.Database, fixture.Blob, BlobReclaimWholeFlowFixture.StoredBytes,
            BlobReclaimWholeFlowFixture.SingleCount, BlobReclaimWholeFlowFixture.EmptyCount);
    }
}
