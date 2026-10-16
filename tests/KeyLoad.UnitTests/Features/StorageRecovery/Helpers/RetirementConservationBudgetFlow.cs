using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class RetirementConservationBudgetFlow
{
    private const int OriginalRows = 2;
    private const int OriginalFloorLookups = 2;
    private const int OnePassWithLifecycleRecords = OriginalRows + OriginalFloorLookups;

    internal static async Task RunAsync(CurrentStorageFixture files, StorageFailureOwnerLifetime ownership)
    {
        StoreIdentity? identity = null;
        await RetirementConservationHeaderFlow.OwnedAsync(files, ownership, store =>
        {
            store.Commit((tx, _) =>
            {
                tx.Put(files.FirstKey, files.FirstValue);
                tx.Put(files.SecondKey, files.SecondValue);
                return true;
            });
            identity = store.Identity;
            store.CreateSnapshot(files.Snapshot, 0L);
            return Task.CompletedTask;
        });
        var image = await File.ReadAllBytesAsync(files.Snapshot);
        var execution = Options.Create(UnitExecutionOptions.StorageExecution().Value with
        {
            MaximumReadCutRecords = OnePassWithLifecycleRecords
        });
        await RetirementConservationHeaderFlow.OwnedAsync(files, ownership, async store =>
        {
            var before = await CurrentStorageSnapshot.CaptureAsync(files.Source);
            await RequireOnePassFitsAsync(files, store, execution);
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => store.InstallSnapshot(files.Snapshot, 0L));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
            await CurrentStorageSnapshot.AssertUnchangedAsync(files.Source, before);
            await RetirementConservationHeaderFlow.RequireAsync(files, store, identity!, healthy: false);
            await Assert.That((await File.ReadAllBytesAsync(files.Snapshot)).SequenceEqual(image)).IsTrue();
            var next = store.Commit((tx, position) => { tx.Put(files.SecondKey, files.FirstValue); return position; });
            await Assert.That(next).IsEqualTo(2L);
            await RetirementConservationHeaderFlow.RequireAsync(files, store, identity!, healthy: true);
        }, execution);
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await RetirementConservationHeaderFlow.OwnedAsync(files, ownership,
                store => RetirementConservationHeaderFlow.RequireAsync(files, store, identity!, healthy: true));
        }
    }
    private static async Task RequireOnePassFitsAsync(CurrentStorageFixture files, ZoneTreeStore store,
        IOptions<ZoneTreeStorageExecutionOptions> execution)
    {
        var options = files.SourceOptions.ResolveExecutionOptions(execution);
        var observed = store.Read(view =>
        {
            var work = new ZoneTreeConservationBudget(options, TimeProvider.System, CancellationToken.None);
            var charged = new ZoneTreeConservationView(view, work);
            var before = ResourceRetirementConservationRead.ReadFloor(charged, options.MaxFrameBytes);
            using var input = File.OpenRead(files.Snapshot);
            _ = ZoneTreeCheckpointReader.ReadMetadata(work.ReadFrame(input));
            input.Position = 0L;
            var snapshot = ZoneTreeCheckpointReader.Read(input, options, work: work);
            var after = ResourceRetirementConservationRead.ReadFloor(charged, options.MaxFrameBytes);
            return (Before: before, After: after, Snapshot: snapshot);
        });
        await Assert.That(observed.Before).IsNull();
        await Assert.That(observed.After).IsNull();
        await Assert.That(observed.Snapshot).IsEqualTo(new StorageSnapshot(store.Identity.Incarnation, 1L, 0L, OriginalRows));
    }

}
