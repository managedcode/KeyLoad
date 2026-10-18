using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed class BlobPublishedLifetimeWholeFlowTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task AcBlobPublishedLifetime001CurrentStateRefusalColdRepairDeleteReclaimReuseAndSecondCold(bool missing)
    {
        using var database = new TestDatabase();
        var fixture = await BlobRangeIntegrityOperations.Publish(database);
        await BlobRangeIntegrityAssertions.Healthy(database.Database, database.Store, fixture);
        var currentCleanup = new ReclaimBlobRequest(Guid.NewGuid(), fixture.Blob, fixture.UploadId, 2);
        var conflict = BlobPublishedLifetimeOperations.Send(database.Database, OperationKind.ReclaimBlob,
            currentCleanup.CommandId, currentCleanup);
        await Assert.That(Errors.Code(conflict.Original.Problem)).IsEqualTo(ErrorCode.Conflict);
        await BlobPublishedLifetimeAssertions.Replay(database.Database, database.Store, conflict);
        var deletion = new DeleteBlobRequest(Guid.NewGuid(), fixture.Blob, 1);
        var cleanup = new ReclaimBlobRequest(Guid.NewGuid(), fixture.Blob, fixture.UploadId, 2);
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var fault = BlobPublishedLifetimeOperations.Damage(database.Store, fixture, missing);
        await Assert.That(database.Store.Position).IsEqualTo(position + 1);
        await BlobPublishedLifetimeAssertions.Refuse(database.Database, database.Store, fixture, deletion, cleanup);
        await BlobPublishedLifetimeAssertions.Refuse(database.Database, database.Store, fixture, deletion, cleanup);
        var damagedImage = QueueWholeFlowStorage.Bytes(database.Store);
        var damagedPosition = database.Store.Position;
        database.Store.Dispose();
        BlobPublishedEmptyLifetime current;
        BlobRangeRetainedOperation reclaimed;
        using (var cold = new ZoneTreeStore(new(database.Directory), database.StorageExecution, UnitExecutionOptions.PointCacheExecution()))
        {
            var owner = database.CreateConfiguredDatabase(cold);
            await BlobWholeFlowAssertions.Unchanged(cold, damagedPosition, damagedImage);
            await BlobPublishedLifetimeAssertions.Refuse(owner, cold, fixture, deletion, cleanup);
            BlobPublishedLifetimeOperations.Repair(cold, fault);
            await BlobWholeFlowAssertions.Unchanged(cold, damagedPosition + 1, image);
            await BlobRangeIntegrityAssertions.Healthy(owner, cold, fixture);
            reclaimed = await BlobPublishedLifetimeCleanup.DeleteAndReclaim(owner, cold, fixture, deletion, cleanup);
            current = await BlobPublishedLifetimeOperations.PublishEmpty(owner, fixture);
            await BlobPublishedLifetimeAssertions.Empty(owner, cold, fixture, current, reclaimed);
            image = QueueWholeFlowStorage.Bytes(cold);
            position = cold.Position;
        }
        using var secondCold = new ZoneTreeStore(new(database.Directory), database.StorageExecution, UnitExecutionOptions.PointCacheExecution());
        var secondOwner = database.CreateConfiguredDatabase(secondCold);
        await BlobWholeFlowAssertions.Unchanged(secondCold, position, image);
        await BlobPublishedLifetimeAssertions.Empty(secondOwner, secondCold, fixture, current, reclaimed);
    }
}
