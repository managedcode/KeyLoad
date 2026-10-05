using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class EpochTargetMutationRetryTests
{
    [Test]
    public async Task AcEpoch003PublishedRetryPreservesCurrentWritesPauseAndReadGeneration()
    {
        using var fixture = new EpochStorageFixture();
        var original = await fixture.CreateNativeSourceAsync(checkpoint: true);
        var sourceBefore = await EpochStorageFixture.CaptureAsync(fixture.Source);
        _ = ZoneTreeFormatUpgrade.Upgrade(fixture.Source, fixture.DestinationOptions(original), UnitExecutionOptions.StorageExecution());
        StoreIdentity advanced;
        using (var current = new ZoneTreeStore(fixture.DestinationOptions(original), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution()))
        {
            current.SetDispatchPaused(false);
            current.Commit((transaction, _) =>
            {
                transaction.Put([0x40, 0x00], [0xA0, 0x00, 0xFF]);
                return true;
            });
            var snapshot = current.CreateSnapshot(fixture.Snapshot);
            current.InstallSnapshot(fixture.Snapshot, snapshot.AppliedPosition);
            advanced = current.Identity;
        }
        var targetBefore = await EpochStorageFixture.CaptureAsync(fixture.Destination);
        var retried = ZoneTreeFormatUpgrade.Upgrade(fixture.Source, fixture.DestinationOptions(original), UnitExecutionOptions.StorageExecution());
        await EpochStorageFixture.AssertIdentityPreservedAsync(advanced, retried);
        await EpochStorageFixture.AssertUnchangedAsync(fixture.Source, sourceBefore);
        await EpochStorageFixture.AssertUnchangedAsync(fixture.Destination, targetBefore);
        using var reopened = new ZoneTreeStore(fixture.DestinationOptions(original), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        await Assert.That(reopened.Position).IsEqualTo(2L);
        await Assert.That(reopened.Read(view => view.ReadOwnedValue([0x40, 0x00]))!)
            .IsEquivalentTo(new byte[] { 0xA0, 0x00, 0xFF }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }
}
