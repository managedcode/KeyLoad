using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed class BlobRangeIntegrityWholeFlowTests
{
    [Test]
    [Arguments(BlobRangeFault.MissingRaw)]
    [Arguments(BlobRangeFault.TruncatedRaw)]
    [Arguments(BlobRangeFault.ChangedUnrequestedByte)]
    [Arguments(BlobRangeFault.UnsupportedPartMetadata)]
    public async Task AcBlob002ColdSecondPartIntegrityRefusalRepairAndSecondColdKeepExactVersion(BlobRangeFault fault)
    {
        using var database = new TestDatabase();
        var fixture = await BlobRangeIntegrityOperations.Publish(database);
        await BlobRangeIntegrityAssertions.Healthy(database.Database, database.Store, fixture);
        var originalPosition = database.Store.Position;
        var originalImage = QueueWholeFlowStorage.Bytes(database.Store);
        var ownedFault = await BlobRangeIntegrityFaults.Install(database.Store, fixture, fault);
        var damagedPosition = database.Store.Position;
        await Assert.That(damagedPosition).IsEqualTo(originalPosition + BlobRangeIntegrityFixture.SingleCount);
        var damagedImage = QueueWholeFlowStorage.Bytes(database.Store);
        database.Store.Dispose();
        using (var cold = new ZoneTreeStore(new(database.Directory), database.StorageExecution,
            UnitExecutionOptions.PointCacheExecution()))
        {
            var owner = database.CreateConfiguredDatabase(cold);
            await BlobWholeFlowAssertions.Unchanged(cold, damagedPosition, damagedImage);
            await BlobRangeIntegrityAssertions.Refused(owner, cold, fixture, fault);
            BlobRangeIntegrityFaults.Repair(cold, ownedFault);
            await BlobWholeFlowAssertions.Unchanged(cold, damagedPosition + BlobRangeIntegrityFixture.SingleCount, originalImage);
            await BlobRangeIntegrityAssertions.Healthy(owner, cold, fixture);
            damagedPosition = cold.Position;
            damagedImage = QueueWholeFlowStorage.Bytes(cold);
        }
        using var secondCold = new ZoneTreeStore(new(database.Directory), database.StorageExecution,
            UnitExecutionOptions.PointCacheExecution());
        var secondOwner = database.CreateConfiguredDatabase(secondCold);
        await BlobWholeFlowAssertions.Unchanged(secondCold, damagedPosition, damagedImage);
        await BlobRangeIntegrityAssertions.Healthy(secondOwner, secondCold, fixture);
        await BlobWholeFlowAssertions.Unchanged(secondCold, damagedPosition, damagedImage);
    }
}
