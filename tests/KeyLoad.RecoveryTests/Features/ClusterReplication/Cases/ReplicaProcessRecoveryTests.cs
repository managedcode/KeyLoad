using KeyLoad.CrashHost;
using KeyLoad.Replication;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests;

/// <summary>AC-REP-002: real process kills after durable acknowledgements, with real ordered recovery.</summary>
internal sealed class ReplicaProcessRecoveryTests
{
    /// <summary>Acknowledged term, vote, append and commit metadata survive abrupt process termination.</summary>
    [Test]
    [Arguments(ReplicaCrashBoundary.TermSaved)]
    [Arguments(ReplicaCrashBoundary.EntryAcknowledged)]
    [Arguments(ReplicaCrashBoundary.CommitAcknowledged)]
    public async Task KillAfterDurableAcknowledgementRecoversOnlyCommittedCanonicalPrefix(ReplicaCrashBoundary boundary)
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var trial = ReplicaProcessTrial.Start(boundary);
        await trial.KillAsync(boundary, cancellationToken);
        using var node = ReplicaCrashNode.OpenTarget(trial.DirectoryPath, trial.Incarnation);
        var committed = boundary == ReplicaCrashBoundary.CommitAcknowledged ? 3L : 2L;
        var last = boundary == ReplicaCrashBoundary.TermSaved ? 2L : 3L;
        await Assert.That(node.Canonical.Identity.NodeId).IsEqualTo(trial.Ready.NodeId);
        await Assert.That(node.Log.State.Incarnation).IsEqualTo(trial.Incarnation);
        await Assert.That(node.Log.State.Term).IsEqualTo(boundary == ReplicaCrashBoundary.TermSaved ? 2L : 1L);
        await Assert.That(node.Log.State.VotedFor).IsEqualTo(boundary == ReplicaCrashBoundary.TermSaved ? ReplicaCrashNode.CandidateId : null);
        await Assert.That(node.Log.State.LastIndex).IsEqualTo(last);
        await Assert.That(node.Log.State.CommittedIndex).IsEqualTo(committed);
        await ReplicaProcessAssertions.DocumentAsync(node, 2);
        if (boundary != ReplicaCrashBoundary.TermSaved)
        {
            await ReplicaProcessAssertions.OperationAsync(node.Database, node.Log.ReadEntry(3)!.Operation, ReplicaCrashModel.Operation(3));
        }
        if (boundary == ReplicaCrashBoundary.TermSaved)
        {
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => node.Log.SaveTermAndVote(2, node.Configuration.LocalId)).Code).IsEqualTo(ErrorCode.Conflict);
        }
        await using var materializer = new ReplicaMaterializer(node.Database, node.Log, node.Snapshots, RecoveryExecutionOptions.Replica());
        await ReplicaProcessAssertions.WaitAsync(materializer, committed, cancellationToken);
        await ReplicaProcessAssertions.DocumentAsync(node, committed);
        await ReplicaProcessAssertions.ReceiptAsync(node, 2);
        if (committed == 3)
        {
            await ReplicaProcessAssertions.ReceiptAsync(node, 3);
        }
        else
        {
            await Assert.That(OutcomeStoreOracle.Read(node.Canonical, ReplicaCrashModel.Operation(3))).IsNull();
        }
        if (boundary == ReplicaCrashBoundary.EntryAcknowledged)
        {
            node.Log.SaveTermAndVote(2, null);
            node.Log.Append([new(3, 2, null)]);
            await Assert.That(node.Log.ReadEntry(3)!.Operation).IsNull();
            await ReplicaProcessAssertions.DocumentAsync(node, 2);
        }
    }

    /// <summary>A corrupt complete checksummed log frame is rejected after a real append ACK and kill.</summary>
    [Test]
    public async Task CompleteReplicaJournalCorruptionAfterAcknowledgedAppendFailsClosed()
    {
        await using var trial = ReplicaProcessTrial.Start(ReplicaCrashBoundary.EntryAcknowledged);
        await trial.KillAsync(ReplicaCrashBoundary.EntryAcknowledged, TestContext.Current!.Execution.CancellationToken);
        var directory = ReplicaCrashNode.TargetStoreDirectories(trial.DirectoryPath)[1];
        var path = Path.Combine(directory, ReplicaProcessFiles.Journal);
        using (var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            file.Position = file.Length - 1;
            var original = file.ReadByte();
            file.Position--;
            file.WriteByte((byte)(original ^ 1));
            RandomAccess.FlushToDisk(file.SafeFileHandle);
        }
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
        {
            using var rejected = new ZoneTreeStore(new(directory) { Incarnation = trial.Incarnation }, RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        }).Code).IsEqualTo(ErrorCode.Corruption);
        using var canonical = new ZoneTreeStore(new(ReplicaCrashNode.TargetStoreDirectories(trial.DirectoryPath)[0])
        { Incarnation = trial.Incarnation }, RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        await Assert.That(canonical.Identity.NodeId).IsEqualTo(trial.Ready.NodeId);
    }
}

/// <summary>AC-REP-004: actual process death on both sides of verified canonical snapshot installation.</summary>
internal sealed class ReplicaSnapshotProcessRecoveryTests
{
    private const string EmptyReplicaDirectory = "empty-replica";

    /// <summary>An empty real node imports the killed trial's verified source image before applying the committed tail.</summary>
    [Test]
    public async Task EmptyReplicaImportsPersistedSnapshotAndReopensWithOrderedCommittedTail()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var trial = ReplicaProcessTrial.Start(ReplicaCrashBoundary.SnapshotVerified);
        await trial.KillAsync(ReplicaCrashBoundary.SnapshotVerified, cancellationToken);
        using var source = ReplicaCrashNode.OpenSource(trial.DirectoryPath, trial.Incarnation);
        source.Snapshots.Recover();
        var image = source.Snapshots.Current!;
        var sourcePosition = source.Canonical.Position;
        var root = Path.Combine(trial.DirectoryPath, EmptyReplicaDirectory);
        Guid nodeId;
        using (var empty = ReplicaCrashNode.OpenTarget(root, trial.Incarnation))
        {
            nodeId = empty.Canonical.Identity.NodeId;
            await Assert.That(empty.Database.LastApplied).IsEqualTo(0);
            await Assert.That(empty.Log.State.CommittedIndex).IsEqualTo(0);
            empty.Log.SaveTermAndVote(1, null);
            var offset = empty.Snapshots.Begin(image);
            await Assert.That(offset).IsEqualTo(0);
            while (offset < image.Length)
            {
                offset = empty.Snapshots.Append(image.TransferId, offset,
                    source.Snapshots.ReadChunk(image.TransferId, offset, empty.Configuration.SnapshotChunkBytes));
            }
            await Assert.That(empty.Snapshots.Complete(image.TransferId)).IsEqualTo(image);
            await FreshCutAsync(empty, nodeId, 4);
            await Assert.That(empty.Canonical.Position).IsEqualTo(sourcePosition);
            await Assert.That(empty.Log.State.LastIndex).IsEqualTo(4);
            await ReplicaProcessAssertions.ReceiptAsync(empty, 4);
            empty.Log.Append([new(5, 1, empty.Database.NormalizeOperation(ReplicaCrashModel.Operation(5)))]);
            await Assert.That(OutcomeStoreOracle.Read(empty.Canonical, ReplicaCrashModel.Operation(5))).IsNull();
            await using var materializer = new ReplicaMaterializer(empty.Database, empty.Log, empty.Snapshots, RecoveryExecutionOptions.Replica());
            materializer.Commit(5);
            await ReplicaProcessAssertions.WaitAsync(materializer, 5, cancellationToken);
            await FreshCutAsync(empty, nodeId, 5);
            await ReplicaProcessAssertions.ReceiptAsync(empty, 5);
        }
        using var reopened = ReplicaCrashNode.OpenTarget(root, trial.Incarnation);
        await using var recovered = new ReplicaMaterializer(reopened.Database, reopened.Log, reopened.Snapshots, RecoveryExecutionOptions.Replica());
        await ReplicaProcessAssertions.WaitAsync(recovered, 5, cancellationToken);
        await FreshCutAsync(reopened, nodeId, 5);
        await ReplicaProcessAssertions.ReceiptAsync(reopened, 4);
        await ReplicaProcessAssertions.ReceiptAsync(reopened, 5);
    }

    private static async Task FreshCutAsync(ReplicaCrashNode node, Guid nodeId, long cut)
    {
        var image = node.Snapshots.Current!;
        await Assert.That(node.Canonical.Identity.NodeId).IsEqualTo(nodeId);
        await Assert.That(node.Canonical.Identity.ReadGeneration).IsEqualTo(1);
        await Assert.That(node.Log.State.CommittedIndex).IsEqualTo(cut);
        await Assert.That(image.Index).IsEqualTo(4);
        await Assert.That(image.Incarnation).IsEqualTo(node.Configuration.Incarnation);
        await Assert.That(File.Exists(ReplicaProcessAssertions.ImagePath(node, image))).IsTrue();
        await ReplicaProcessAssertions.DocumentAsync(node, cut);
    }

    /// <summary>Verified interrupted transfer recovery publishes cut four, retains tail five and preserves outcomes.</summary>
    [Test]
    [Arguments(ReplicaCrashBoundary.SnapshotVerified)]
    [Arguments(ReplicaCrashBoundary.SnapshotInstalled)]
    public async Task KillAtVerifiedSnapshotBoundaryRecoversExactCutAndOrderedTail(ReplicaCrashBoundary boundary)
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var trial = ReplicaProcessTrial.Start(boundary);
        await trial.KillAsync(boundary, cancellationToken);
        ReplicaSnapshot published;
        using (var node = ReplicaCrashNode.OpenTarget(trial.DirectoryPath, trial.Incarnation))
        {
            await Assert.That(node.Log.State.Snapshot).IsNull();
            await Assert.That(node.Log.State.CommittedIndex).IsEqualTo(3);
            await Assert.That(node.Log.State.LastIndex).IsEqualTo(5);
            await ReplicaProcessAssertions.DocumentAsync(node, boundary == ReplicaCrashBoundary.SnapshotVerified ? 3 : 4);
            await Assert.That(node.Canonical.Identity.ReadGeneration).IsEqualTo(boundary == ReplicaCrashBoundary.SnapshotVerified ? 0 : 1);
            await using var materializer = new ReplicaMaterializer(node.Database, node.Log, node.Snapshots, RecoveryExecutionOptions.Replica());
            published = node.Snapshots.Current!;
            await ReplicaProcessAssertions.SnapshotAsync(node, trial, 4);
            await Assert.That(node.Log.TermAt(4)).IsEqualTo(1);
            await Assert.That(node.Log.ReadEntry(4)).IsNull();
            await ReplicaProcessAssertions.OperationAsync(node.Database, node.Log.ReadEntry(5)!.Operation, ReplicaCrashModel.Operation(5));
            await Assert.That(OutcomeStoreOracle.Read(node.Canonical, ReplicaCrashModel.Operation(5))).IsNull();
            await ReplicaProcessAssertions.ReceiptAsync(node, 4);
            materializer.Commit(5);
            await ReplicaProcessAssertions.WaitAsync(materializer, 5, cancellationToken);
            await ReplicaProcessAssertions.DocumentAsync(node, 5);
            await ReplicaProcessAssertions.ReceiptAsync(node, 5);
        }
        using var reopened = ReplicaCrashNode.OpenTarget(trial.DirectoryPath, trial.Incarnation);
        await using var recovered = new ReplicaMaterializer(reopened.Database, reopened.Log, reopened.Snapshots, RecoveryExecutionOptions.Replica());
        await ReplicaProcessAssertions.WaitAsync(recovered, 5, cancellationToken);
        await ReplicaProcessAssertions.SnapshotAsync(reopened, trial, 5);
        await Assert.That(reopened.Snapshots.Current).IsEqualTo(published);
        await Assert.That(reopened.Log.State.CommittedIndex).IsEqualTo(5);
        await ReplicaProcessAssertions.ReceiptAsync(reopened, 4);
        await ReplicaProcessAssertions.ReceiptAsync(reopened, 5);
    }

    /// <summary>Missing or corrupt published images fail closed without replacing a newer committed canonical cut.</summary>
    [Test]
    [Arguments(ReplicaCrashBoundary.SnapshotVerified, false)]
    [Arguments(ReplicaCrashBoundary.SnapshotVerified, true)]
    [Arguments(ReplicaCrashBoundary.SnapshotInstalled, false)]
    [Arguments(ReplicaCrashBoundary.SnapshotInstalled, true)]
    public async Task PublishedImageDamageFailsClosedAndPreservesNewerCanonicalState(ReplicaCrashBoundary boundary, bool missing)
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var trial = ReplicaProcessTrial.Start(boundary);
        await trial.KillAsync(boundary, cancellationToken);
        ReplicaSnapshot published;
        string path;
        using (var node = ReplicaCrashNode.OpenTarget(trial.DirectoryPath, trial.Incarnation))
        {
            await using var materializer = new ReplicaMaterializer(node.Database, node.Log, node.Snapshots, RecoveryExecutionOptions.Replica());
            published = node.Snapshots.Current!;
            path = ReplicaProcessAssertions.ImagePath(node, published);
            materializer.Commit(5);
            await ReplicaProcessAssertions.WaitAsync(materializer, 5, cancellationToken);
        }
        if (missing)
        { File.Delete(path); }
        else
        { File.WriteAllBytes(path, [1]); }
        using var reopened = ReplicaCrashNode.OpenTarget(trial.DirectoryPath, trial.Incarnation);
        if (missing)
        { Assert.ThrowsExactly<FileNotFoundException>(reopened.Snapshots.Recover); }
        else
        { await Assert.That(Assert.ThrowsExactly<KeyLoadException>(reopened.Snapshots.Recover).Code).IsEqualTo(ErrorCode.Corruption); }
        await Assert.That(reopened.Snapshots.Current).IsEqualTo(published);
        await Assert.That(reopened.Log.State.CommittedIndex).IsEqualTo(5);
        await Assert.That(reopened.Canonical.Identity.NodeId).IsEqualTo(trial.Ready.NodeId);
        await Assert.That(reopened.Canonical.Identity.ReadGeneration).IsEqualTo(1);
        await ReplicaProcessAssertions.DocumentAsync(reopened, 5);
        await ReplicaProcessAssertions.ReceiptAsync(reopened, 5);
    }
}
