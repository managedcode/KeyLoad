using KeyLoad.CrashHost;
using KeyLoad.Replication;
using KeyLoad.Server;

namespace KeyLoad.RecoveryTests.Features.ClusterReplication;

internal static class ReplicaSnapshotInstallFencingFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var owners = new ReplicaSnapshotInstallFencingOwners();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var trial = owners.Start();
                await trial.KillAsync(ReplicaCrashBoundary.SnapshotChunkAcknowledged, token).ConfigureAwait(false);
                var source = owners.OpenSource();
                source.Snapshots.Recover();
                var image = source.Snapshots.Current ?? throw new InvalidOperationException(ReplicaProtocol.SnapshotUnavailable);
                await ReplicaPrefixGcReceiptAssertions.DocumentAsync(source, ReplicaSnapshotInstallFencingProtocol.SnapshotCut);
                var initial = await InitialAsync(owners, trial, source, image);
                var installed = await ResumeColdAsync(owners, trial, source, image, initial, token);
                await FinalColdAsync(owners, trial, image, installed, token);
            }, failures).ConfigureAwait(false);
            if (failures.Count != 0)
            {
                owners.RetainEvidence();
            }
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static Task<ReplicaSnapshotInstallFencingCut> InitialAsync(ReplicaSnapshotInstallFencingOwners owners, ReplicaProcessTrial trial,
        ReplicaCrashNode source, ReplicaSnapshot image)
        => owners.TargetAsync(async node =>
        {
            await ReplicaSnapshotInstallFencingState.PrefixAsync(node, image, trial);
            var before = ReplicaSnapshotInstallFencingState.Capture(node);
            await ReplicaSnapshotInstallFencingRefusals.RequireAsync(node, source, image);
            return before;
        });

    private static Task<ReplicaSnapshotInstallFencingCut> ResumeColdAsync(ReplicaSnapshotInstallFencingOwners owners, ReplicaProcessTrial trial,
        ReplicaCrashNode source, ReplicaSnapshot image, ReplicaSnapshotInstallFencingCut original, CancellationToken token)
        => owners.TargetAsync(async node =>
        {
            await ReplicaSnapshotInstallFencingState.EqualAsync(original, ReplicaSnapshotInstallFencingState.Capture(node));
            node.Snapshots.Recover();
            await ReplicaSnapshotInstallFencingState.EqualAsync(original, ReplicaSnapshotInstallFencingState.Capture(node));
            await ReplicaSnapshotInstallFencingState.PrefixAsync(node, image, trial);
            await ReplicaSnapshotInstallFencingRefusals.RequireAsync(node, source, image);
            await ResumeAsync(node, source, image, token);
            await owners.MaterializerAsync(node, async materializer =>
            {
                materializer.Commit(ReplicaSnapshotInstallFencingProtocol.TailCut);
                await ReplicaProcessAssertions.WaitAsync(materializer, ReplicaSnapshotInstallFencingProtocol.TailCut, token);
                await ReplicaProcessAssertions.SnapshotAsync(node, trial, ReplicaSnapshotInstallFencingProtocol.TailCut);
                await ReplicaPrefixGcReceiptAssertions.AllOriginalAsync(node, ReplicaSnapshotInstallFencingProtocol.TailCut);
            });
            return ReplicaSnapshotInstallFencingState.Capture(node);
        });

    private static async Task ResumeAsync(ReplicaCrashNode node, ReplicaCrashNode source,
        ReplicaSnapshot image, CancellationToken token)
    {
        var offset = node.Snapshots.Begin(image);
        await Assert.That(offset).IsEqualTo((long)ReplicaCrashTransfer.PrefixBytes);
        var prefix = source.Snapshots.ReadChunk(image.TransferId, ReplicaSnapshotInstallFencingProtocol.FirstOffset,
            ReplicaCrashTransfer.PrefixBytes);
        await Assert.That(node.Snapshots.Append(image.TransferId, ReplicaSnapshotInstallFencingProtocol.FirstOffset,
            prefix)).IsEqualTo(offset);
        while (offset < image.Length)
        {
            token.ThrowIfCancellationRequested();
            offset = node.Snapshots.Append(image.TransferId, offset,
                source.Snapshots.ReadChunk(image.TransferId, offset, node.Configuration.SnapshotChunkBytes));
        }
        await Assert.That(offset).IsEqualTo(image.Length);
        await Assert.That(node.Snapshots.Complete(image.TransferId, token)).IsEqualTo(image);
        await ReplicaPrefixGcReceiptAssertions.DocumentAsync(node, ReplicaSnapshotInstallFencingProtocol.SnapshotCut);
        await ReplicaPrefixGcReceiptAssertions.AllOriginalAsync(node, ReplicaSnapshotInstallFencingProtocol.SnapshotCut);
    }

    private static Task FinalColdAsync(ReplicaSnapshotInstallFencingOwners owners, ReplicaProcessTrial trial, ReplicaSnapshot image,
        ReplicaSnapshotInstallFencingCut installed, CancellationToken token)
        => owners.TargetAsync(async node =>
            await owners.MaterializerAsync(node, async materializer =>
            {
                await ReplicaProcessAssertions.WaitAsync(materializer, ReplicaSnapshotInstallFencingProtocol.TailCut, token);
                await ReplicaSnapshotInstallFencingState.EqualAsync(installed, ReplicaSnapshotInstallFencingState.Capture(node));
                await Assert.That(node.Snapshots.Current).IsEqualTo(image);
                await ReplicaProcessAssertions.SnapshotAsync(node, trial, ReplicaSnapshotInstallFencingProtocol.TailCut);
                await ReplicaPrefixGcReceiptAssertions.AllOriginalAsync(node, ReplicaSnapshotInstallFencingProtocol.TailCut);
                await ReplicaSnapshotInstallFencingHealthy.RunAsync(node, materializer, token);
            }));
}
