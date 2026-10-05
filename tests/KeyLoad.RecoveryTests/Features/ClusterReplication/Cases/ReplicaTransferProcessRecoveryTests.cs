using KeyLoad.CrashHost;
using KeyLoad.Replication;

namespace KeyLoad.RecoveryTests;

/// <summary>AC-REP-004: real kills retain resumable private transfers, safely reclaim rejected images and reopen published cuts.</summary>
internal sealed class ReplicaTransferProcessRecoveryTests
{
    /// <summary>Transfer intent, prefix ACK and failed verification preserve cut three until a verified retry installs cut four.</summary>
    [Test]
    [Arguments(ReplicaCrashBoundary.SnapshotTransferBegun)]
    [Arguments(ReplicaCrashBoundary.SnapshotChunkAcknowledged)]
    [Arguments(ReplicaCrashBoundary.SnapshotRejected)]
    public async Task KilledPrivateTransferResumesOrReclaimsWithoutChangingCanonicalCut(ReplicaCrashBoundary boundary)
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var trial = ReplicaProcessTrial.Start(boundary);
        await trial.KillAsync(boundary, cancellationToken);
        using var source = ReplicaCrashNode.OpenSource(trial.DirectoryPath, trial.Incarnation);
        source.Snapshots.Recover();
        var image = source.Snapshots.Current!;
        using (var node = ReplicaCrashNode.OpenTarget(trial.DirectoryPath, trial.Incarnation))
        {
            await InspectAndRecoverAsync(node, trial, image, boundary);
            await ResumeAsync(node, source, image, boundary);
            await Assert.That(node.Snapshots.Complete(image.TransferId)).IsEqualTo(image);
            await ReplicaProcessAssertions.SnapshotAsync(node, trial, 4);
            await Assert.That(node.Log.State.CommittedIndex).IsEqualTo(4);
            await ReplicaProcessAssertions.ReceiptAsync(node, 4);
            await using var materializer = new ReplicaMaterializer(node.Database, node.Log, node.Snapshots, RecoveryExecutionOptions.Replica());
            materializer.Commit(5);
            await ReplicaProcessAssertions.WaitAsync(materializer, 5, cancellationToken);
            await ReplicaProcessAssertions.DocumentAsync(node, 5);
            await ReplicaProcessAssertions.ReceiptAsync(node, 5);
        }
        using var reopened = ReplicaCrashNode.OpenTarget(trial.DirectoryPath, trial.Incarnation);
        await using var recovered = new ReplicaMaterializer(reopened.Database, reopened.Log, reopened.Snapshots, RecoveryExecutionOptions.Replica());
        await ReplicaProcessAssertions.WaitAsync(recovered, 5, cancellationToken);
        await ReplicaProcessAssertions.SnapshotAsync(reopened, trial, 5);
        await ReplicaProcessAssertions.ReceiptAsync(reopened, 4);
        await ReplicaProcessAssertions.ReceiptAsync(reopened, 5);
        await Assert.That(File.Exists(ReplicaProcessAssertions.ImagePath(source, image))).IsTrue();
    }

    /// <summary>A kill after immutable image and metadata publication reopens cut four without a following append.</summary>
    [Test]
    public async Task KillAfterSnapshotPublicationReopensAcknowledgedCutBeforeApplyingRetainedTail()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var trial = ReplicaProcessTrial.Start(ReplicaCrashBoundary.SnapshotPublished);
        await trial.KillAsync(ReplicaCrashBoundary.SnapshotPublished, cancellationToken);
        ReplicaSnapshot published;
        using (var node = ReplicaCrashNode.OpenTarget(trial.DirectoryPath, trial.Incarnation))
        {
            published = node.Snapshots.Current!;
            await Assert.That(node.Log.State.CommittedIndex).IsEqualTo(4);
            await ReplicaProcessAssertions.SnapshotAsync(node, trial, 4);
            node.Snapshots.Recover();
            await ReplicaProcessAssertions.SnapshotAsync(node, trial, 4);
            await ReplicaProcessAssertions.ReceiptAsync(node, 4);
            await Assert.That(OutcomeStoreOracle.Read(node.Store, ReplicaCrashModel.Operation(5))).IsNull();
        }
        using var reopened = ReplicaCrashNode.OpenTarget(trial.DirectoryPath, trial.Incarnation);
        await using var recovered = new ReplicaMaterializer(reopened.Database, reopened.Log, reopened.Snapshots, RecoveryExecutionOptions.Replica());
        await ReplicaProcessAssertions.SnapshotAsync(reopened, trial, 4);
        await Assert.That(reopened.Snapshots.Current).IsEqualTo(published);
        recovered.Commit(5);
        await ReplicaProcessAssertions.WaitAsync(recovered, 5, cancellationToken);
        await ReplicaProcessAssertions.DocumentAsync(reopened, 5);
        await ReplicaProcessAssertions.ReceiptAsync(reopened, 4);
        await ReplicaProcessAssertions.ReceiptAsync(reopened, 5);
    }

    private static async Task InspectAndRecoverAsync(ReplicaCrashNode node, ReplicaProcessTrial trial,
        ReplicaSnapshot image, ReplicaCrashBoundary boundary)
    {
        await ReplicaProcessAssertions.DocumentAsync(node, 3);
        await Assert.That(node.Canonical.Identity.NodeId).IsEqualTo(trial.Ready.NodeId);
        await Assert.That(node.Canonical.Identity.ReadGeneration).IsEqualTo(0);
        await Assert.That(node.Log.State.Snapshot).IsNull();
        await Assert.That(node.Log.State.CommittedIndex).IsEqualTo(3);
        await Assert.That(node.Log.State.LastIndex).IsEqualTo(5);
        await Assert.That(node.HasObsoleteRecord()).IsTrue();
        await Assert.That(File.Exists(IncomingPath(node, ReplicaProtocol.IncomingManifest))).IsTrue();
        var expected = boundary switch
        {
            ReplicaCrashBoundary.SnapshotTransferBegun => 0L,
            ReplicaCrashBoundary.SnapshotChunkAcknowledged => ReplicaCrashTransfer.PrefixBytes,
            _ => image.Length
        };
        await Assert.That(new FileInfo(IncomingPath(node, ReplicaProtocol.IncomingImage)).Length).IsEqualTo(expected);
        node.Snapshots.Recover();
        await ReplicaProcessAssertions.DocumentAsync(node, 3);
        await Assert.That(node.Canonical.Identity.ReadGeneration).IsEqualTo(0);
        await Assert.That(node.Log.State.CommittedIndex).IsEqualTo(3);
        await Assert.That(node.Snapshots.Current).IsNull();
        await Assert.That(File.Exists(ReplicaProcessAssertions.ImagePath(node, image))).IsFalse();
        var retained = boundary != ReplicaCrashBoundary.SnapshotRejected;
        await Assert.That(File.Exists(IncomingPath(node, ReplicaProtocol.IncomingImage))).IsEqualTo(retained);
        await Assert.That(File.Exists(IncomingPath(node, ReplicaProtocol.IncomingManifest))).IsEqualTo(retained);
    }

    private static async Task ResumeAsync(ReplicaCrashNode node, ReplicaCrashNode source,
        ReplicaSnapshot image, ReplicaCrashBoundary boundary)
    {
        var expected = boundary == ReplicaCrashBoundary.SnapshotChunkAcknowledged ? ReplicaCrashTransfer.PrefixBytes : 0L;
        var offset = node.Snapshots.Begin(image);
        await Assert.That(offset).IsEqualTo(expected);
        await Assert.That(image.Length).IsGreaterThan((long)ReplicaCrashTransfer.PrefixBytes);
        if (expected > 0)
        {
            var prefix = source.Snapshots.ReadChunk(image.TransferId, 0, ReplicaCrashTransfer.PrefixBytes);
            await Assert.That(node.Snapshots.Append(image.TransferId, 0, prefix)).IsEqualTo(expected);
            prefix[^1] ^= 1;
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => node.Snapshots.Append(image.TransferId, 0, prefix)).Code).IsEqualTo(ErrorCode.Conflict);
            await Assert.That(node.Snapshots.Begin(image)).IsEqualTo(expected);
        }
        while (offset < image.Length)
        {
            offset = node.Snapshots.Append(image.TransferId, offset,
                source.Snapshots.ReadChunk(image.TransferId, offset, node.Configuration.SnapshotChunkBytes));
        }
        await Assert.That(offset).IsEqualTo(image.Length);
    }

    private static string IncomingPath(ReplicaCrashNode node, string name)
        => Path.Combine(node.Configuration.Directory, ReplicaProtocol.SnapshotDirectory, name);
}
