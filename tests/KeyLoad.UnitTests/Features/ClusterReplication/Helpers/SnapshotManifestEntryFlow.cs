using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class SnapshotManifestEntryFlow
{
    internal static async Task RunAsync(string variant, CancellationToken token)
    {
        var owner = new SnapshotManifestEntryOwners();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var state = await SnapshotManifestEntrySeed.RunAsync(owner, token);
            await SnapshotManifestEntryRefusal.RunAsync(owner, state, variant, token);
            var healthy = await SnapshotManifestEntryInstall.RunAsync(owner, state, token);
            await SnapshotManifestEntryCold.RunAsync(owner, healthy, token);
            var source = owner.Open(source: true);
            source.Snapshots.Recover();
            await Assert.That(source.Snapshots.Current).IsEqualTo(state.Snapshot);
        }, failures);
        if (failures.Count != 0)
        {
            owner.RetainRoot();
        }
        await owner.CleanupAsync(failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
