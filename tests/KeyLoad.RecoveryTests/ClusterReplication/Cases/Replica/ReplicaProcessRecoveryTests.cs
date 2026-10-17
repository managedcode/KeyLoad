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
            using var rejected = new ZoneTreeStore(new(directory)
            {
                Incarnation = trial.Incarnation
            }, RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        }).Code).IsEqualTo(ErrorCode.Corruption);
        using var canonical = new ZoneTreeStore(new(ReplicaCrashNode.TargetStoreDirectories(trial.DirectoryPath)[0])
        {
            Incarnation = trial.Incarnation
        }, RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        await Assert.That(canonical.Identity.NodeId).IsEqualTo(trial.Ready.NodeId);
    }
}
