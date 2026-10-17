using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.ClusterReplication;

internal sealed class ReplicaPrefixGcProcessRecoveryTests
{
    [Test]
    [Arguments(CommitStage.HeaderWritten, 0)]
    [Arguments(CommitStage.PayloadWritten, 0)]
    [Arguments(CommitStage.JournalFlushed, 0)]
    [Arguments(CommitStage.MutationApplied, 0)]
    [Arguments(CommitStage.MutationApplied, 3)]
    [Arguments(CommitStage.ApplyCompleted, 0)]
    [Arguments(CommitStage.SnapshotWritten, 0)]
    [Arguments(CommitStage.SnapshotFlushed, 0)]
    [Arguments(CommitStage.InstallPrepared, 0)]
    [Arguments(CommitStage.JournalSwapped, 0)]
    public async Task AcRepGc003ActualPrefixDeletionAndReplicaRewriteRetainVerifiedSnapshotTailRecovery(
        CommitStage stage, int index)
        => await ReplicaPrefixGcCrashTrial.RunAsync(stage, index, TestContext.Current!.Execution.CancellationToken);
}
