using KeyLoad.CrashHost;
using KeyLoad.Replication;
using KeyLoad.Server;

namespace KeyLoad.RecoveryTests.Features.ClusterReplication;

internal static class ReplicaSnapshotInstallBoundaryFlow
{
    private const int OriginalCut = 3;
    private const int SnapshotCut = 4;
    private const int TailCut = 5;
    private const long OneByte = 1;

    internal static async Task RunAsync(bool canceled, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var trial = ReplicaProcessTrial.Start(ReplicaCrashBoundary.SnapshotChunkAcknowledged);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await trial.KillAsync(ReplicaCrashBoundary.SnapshotChunkAcknowledged, token);
                await TransferAsync(trial, canceled, token);
                await ReplicaPrefixGcOwners.NodeAsync(trial.DirectoryPath, trial.Incarnation, node =>
                    ReplicaPrefixGcOwners.MaterializerAsync(node, async materializer =>
                    {
                        await ReplicaProcessAssertions.WaitAsync(materializer, TailCut, token);
                        await ReplicaProcessAssertions.SnapshotAsync(node, trial, TailCut);
                        await ReplicaPrefixGcReceiptAssertions.VerifyAsync(node, SnapshotCut);
                        await ReplicaPrefixGcReceiptAssertions.VerifyAsync(node, TailCut);
                    }));
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task TransferAsync(ReplicaProcessTrial trial, bool canceled, CancellationToken token)
    {
        var failures = new List<Exception>();
        ReplicaCrashNode? source = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var active = ReplicaCrashNode.OpenSource(trial.DirectoryPath, trial.Incarnation);
            source = active;
            active.Snapshots.Recover();
            var image = active.Snapshots.Current ?? throw new InvalidOperationException();
            await ReplicaPrefixGcOwners.NodeAsync(trial.DirectoryPath, trial.Incarnation, node =>
                ReplicaPrefixGcOwners.MaterializerAsync(node, materializer =>
                    InstallAsync(active, node, materializer, image, canceled, token)));
        }, failures);
        if (source is { } owned)
        { ServerFailureObserver.Observe(owned.Dispose, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task InstallAsync(ReplicaCrashNode source, ReplicaCrashNode node,
        ReplicaMaterializer materializer, ReplicaSnapshot image, bool canceled, CancellationToken token)
    {
        await ReplicaProcessAssertions.DocumentAsync(node, OriginalCut);
        var offset = node.Snapshots.Begin(image);
        await Assert.That(offset).IsEqualTo((long)ReplicaCrashTransfer.PrefixBytes);
        while (offset < image.Length)
        {
            token.ThrowIfCancellationRequested();
            offset = node.Snapshots.Append(image.TransferId, offset,
                source.Snapshots.ReadChunk(image.TransferId, offset, node.Configuration.SnapshotChunkBytes));
        }
        var before = ReplicaSnapshotInstallBoundaryAssertions.Capture(node);
        if (canceled)
        {
            using var original = CancellationTokenSource.CreateLinkedTokenSource(token);
            await original.CancelAsync();
            var failure = await Assert.ThrowsAsync<OperationCanceledException>(() =>
                materializer.InstallCheckpointAsync(image.TransferId, original.Token)) ?? throw new InvalidOperationException();
            await Assert.That(failure.CancellationToken).IsEqualTo(original.Token);
        }
        else
        { await RejectCeilingAsync(node, image); }
        await ReplicaSnapshotInstallBoundaryAssertions.UnchangedAsync(node, before);
        await Assert.That(await materializer.InstallCheckpointAsync(image.TransferId, token)).IsEqualTo(image);
        await ReplicaProcessAssertions.DocumentAsync(node, SnapshotCut);
        await ReplicaPrefixGcReceiptAssertions.VerifyAsync(node, SnapshotCut);
        materializer.Commit(TailCut);
        await ReplicaProcessAssertions.WaitAsync(materializer, TailCut, token);
        await ReplicaProcessAssertions.DocumentAsync(node, TailCut);
        await ReplicaPrefixGcReceiptAssertions.VerifyAsync(node, SnapshotCut);
        await ReplicaPrefixGcReceiptAssertions.VerifyAsync(node, TailCut);
    }

    private static async Task RejectCeilingAsync(ReplicaCrashNode node, ReplicaSnapshot image)
    {
        await Assert.That(image.Length).IsGreaterThan(OneByte);
        var bound = checked(image.Length - OneByte);
        var configuration = node.Configuration with
        {
            MaxSnapshotBytes = bound,
            SnapshotChunkBytes = checked((int)Math.Min(node.Configuration.SnapshotChunkBytes, bound))
        };
        var receiver = new ReplicaSnapshotStore(node.Canonical, node.Log,
            RecoveryExecutionOptions.Configuration(configuration), RecoveryExecutionOptions.Replica());
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => receiver.Begin(image));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
    }
}
