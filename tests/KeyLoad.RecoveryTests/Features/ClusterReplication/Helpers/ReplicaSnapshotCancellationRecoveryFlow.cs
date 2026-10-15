using KeyLoad.CrashHost;
using KeyLoad.Replication;
using KeyLoad.Server;

namespace KeyLoad.RecoveryTests.Features.ClusterReplication;

internal static class ReplicaSnapshotCancellationRecoveryFlow
{
    private const int NoFailures = 0;

    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var owners = new ReplicaSnapshotInstallFencingOwners();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var trial = owners.Start();
                await trial.KillAsync(ReplicaCrashBoundary.SnapshotChunkAcknowledged, token);
                var source = owners.OpenSource();
                source.Snapshots.Recover();
                var image = source.Snapshots.Current ?? throw new InvalidOperationException(ReplicaProtocol.SnapshotUnavailable);
                await ReplicaPrefixGcReceiptAssertions.DocumentAsync(source, ReplicaSnapshotInstallFencingProtocol.SnapshotCut);
                var partial = await InitialAsync(owners, trial, image, token);
                var installed = await ResumeColdAsync(owners, trial, source, image, partial, token);
                await FinalColdAsync(owners, trial, image, installed, token);
            }, failures);
            if (failures.Count != NoFailures)
            {
                owners.RetainEvidence();
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static Task<ReplicaSnapshotCancellationRecoveryCut> InitialAsync(ReplicaSnapshotInstallFencingOwners owners,
        ReplicaProcessTrial trial, ReplicaSnapshot image, CancellationToken token)
        => owners.TargetAsync(async node =>
        {
            await ReplicaSnapshotInstallFencingState.PrefixAsync(node, image, trial);
            var original = ReplicaSnapshotCancellationRecoveryAssertions.Capture(node);
            await owners.MaterializerAsync(node, async materializer =>
            {
                await ReplicaSnapshotCancellationRecoveryAssertions.CancelAsync(node,
                    cancellation => materializer.BeginCheckpointAsync(image, cancellation), token);
                await Assert.That(await materializer.BeginCheckpointAsync(image, token))
                    .IsEqualTo((long)ReplicaCrashTransfer.PrefixBytes);
                await ReplicaSnapshotCancellationRecoveryAssertions.EqualAsync(original,
                    ReplicaSnapshotCancellationRecoveryAssertions.Capture(node));
            });
            return original;
        });

    private static Task<ReplicaSnapshotCancellationRecoveryCut> ResumeColdAsync(ReplicaSnapshotInstallFencingOwners owners,
        ReplicaProcessTrial trial, ReplicaCrashNode source, ReplicaSnapshot image,
        ReplicaSnapshotCancellationRecoveryCut original, CancellationToken token)
        => owners.TargetAsync(async node =>
        {
            await ReplicaSnapshotCancellationRecoveryAssertions.EqualAsync(original,
                ReplicaSnapshotCancellationRecoveryAssertions.Capture(node));
            node.Snapshots.Recover();
            await ReplicaSnapshotCancellationRecoveryAssertions.EqualAsync(original,
                ReplicaSnapshotCancellationRecoveryAssertions.Capture(node));
            await ReplicaSnapshotInstallFencingState.PrefixAsync(node, image, trial);
            await owners.MaterializerAsync(node, async materializer =>
            {
                await TransferAsync(node, source, materializer, image, token);
                await ReplicaSnapshotCancellationRecoveryAssertions.CancelAsync(node,
                    cancellation => materializer.InstallCheckpointAsync(image.TransferId, cancellation), token);
                await Assert.That(await materializer.InstallCheckpointAsync(image.TransferId, token)).IsEqualTo(image);
                await ReplicaProcessAssertions.SnapshotAsync(node, trial, ReplicaSnapshotInstallFencingProtocol.SnapshotCut);
                await ReplicaPrefixGcReceiptAssertions.AllOriginalAsync(node, ReplicaSnapshotInstallFencingProtocol.SnapshotCut);
                materializer.Commit(ReplicaSnapshotInstallFencingProtocol.TailCut);
                await ReplicaProcessAssertions.WaitAsync(materializer, ReplicaSnapshotInstallFencingProtocol.TailCut, token);
                await ReplicaProcessAssertions.SnapshotAsync(node, trial, ReplicaSnapshotInstallFencingProtocol.TailCut);
                await ReplicaPrefixGcReceiptAssertions.AllOriginalAsync(node, ReplicaSnapshotInstallFencingProtocol.TailCut);
            });
            return ReplicaSnapshotCancellationRecoveryAssertions.Capture(node);
        });

    private static async Task TransferAsync(ReplicaCrashNode node, ReplicaCrashNode source,
        ReplicaMaterializer materializer, ReplicaSnapshot image, CancellationToken token)
    {
        var offset = await materializer.BeginCheckpointAsync(image, token);
        await Assert.That(offset).IsEqualTo((long)ReplicaCrashTransfer.PrefixBytes);
        var prefix = source.Snapshots.ReadChunk(image.TransferId, ReplicaSnapshotInstallFencingProtocol.FirstOffset,
            ReplicaCrashTransfer.PrefixBytes);
        await Assert.That(node.Snapshots.Append(image.TransferId, ReplicaSnapshotInstallFencingProtocol.FirstOffset, prefix))
            .IsEqualTo(offset);
        while (offset < image.Length)
        {
            token.ThrowIfCancellationRequested();
            offset = node.Snapshots.Append(image.TransferId, offset,
                source.Snapshots.ReadChunk(image.TransferId, offset, node.Configuration.SnapshotChunkBytes));
        }
        await Assert.That(offset).IsEqualTo(image.Length);
        await ReplicaPrefixGcReceiptAssertions.DocumentAsync(node, ReplicaSnapshotInstallFencingProtocol.OriginalCut);
        await Assert.That(node.Snapshots.Current).IsNull();
    }

    private static Task FinalColdAsync(ReplicaSnapshotInstallFencingOwners owners, ReplicaProcessTrial trial,
        ReplicaSnapshot image, ReplicaSnapshotCancellationRecoveryCut installed, CancellationToken token)
        => owners.TargetAsync(async node =>
            await owners.MaterializerAsync(node, async materializer =>
            {
                await ReplicaProcessAssertions.WaitAsync(materializer, ReplicaSnapshotInstallFencingProtocol.TailCut, token);
                await ReplicaSnapshotCancellationRecoveryAssertions.EqualAsync(installed,
                    ReplicaSnapshotCancellationRecoveryAssertions.Capture(node));
                await Assert.That(node.Snapshots.Current).IsEqualTo(image);
                await ReplicaProcessAssertions.SnapshotAsync(node, trial, ReplicaSnapshotInstallFencingProtocol.TailCut);
                await ReplicaPrefixGcReceiptAssertions.AllOriginalAsync(node, ReplicaSnapshotInstallFencingProtocol.TailCut);
                await ReplicaSnapshotInstallFencingHealthy.RunAsync(node, materializer, token);
            }));
}
