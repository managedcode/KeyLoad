using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementCleanupNativeData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementCleanupNativeFlow
{
    internal static async Task RunAsync(TestDatabase fixture, CancellationToken token)
    {
        var state = ResourceRetirementCleanupNativeSeed.Create(fixture);
        await ResourceRetirementCleanupNativeImages.SeededAsync(state);
        var limits = fixture.Database.Limits with
        {
            MaxBatchMutations = MutationLimit,
            MaxScanRecords = MutationLimit
        };
        await ResourceRetirementCleanupNativeRefusals.RunAsync(fixture, state, limits);
        var progress = await ResourceRetirementCleanupNativePages.CommitCheckedAsync(fixture.Store, state,
            state.Partitions[FirstIndex], limits);
        await Assert.That(progress.HasMore).IsTrue();
        await Assert.That(progress.DeletedRecords).IsEqualTo(One);
        var image = ResourceRetirementCleanupNativeImages.Read(fixture.Store);
        var position = fixture.Store.Position;
        var identity = fixture.Store.Identity;
        fixture.JoinOwnedResources();
        await ResourceIncarnationCold.OpenAsync(fixture, identity, async store =>
        {
            await ResourceRetirementCleanupNativeImages.SameAsync(store, image, position);
            await ResourceRetirementCleanupNativePages.CompleteAsync(store, state, state.Partitions[FirstIndex], limits, token);
            var partial = await ResourceRetirementCleanupNativePages.CommitCheckedAsync(store, state, state.Partitions[SecondIndex], limits);
            await Assert.That(partial.HasMore).IsTrue();
            image = ResourceRetirementCleanupNativeImages.Read(store);
            position = store.Position;
        });
        await ResourceIncarnationCold.OpenAsync(fixture, identity, async store =>
        {
            await ResourceRetirementCleanupNativeImages.SameAsync(store, image, position);
            await ResourceRetirementCleanupNativePages.CompleteAsync(store, state, state.Partitions[SecondIndex], limits, token);
            await ResourceRetirementCleanupNativeImages.FinalAsync(store, state);
        });
    }
}
