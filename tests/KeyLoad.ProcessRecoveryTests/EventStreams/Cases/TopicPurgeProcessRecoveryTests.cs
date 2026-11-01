using KeyLoad.Storage.ZoneTree;
namespace KeyLoad.ProcessRecoveryTests.EventStreams;

internal sealed class TopicPurgeProcessRecoveryTests
{
    [Test]
    [Arguments(CommitStage.WriteAheadLogSynced, 0, false)]
    [Arguments(CommitStage.MutationApplied, 0, false)]
    [Arguments(CommitStage.ApplyCompleted, 0, false)]
    [Arguments(CommitStage.WriteAheadLogSynced, 0, true)]
    [Arguments(CommitStage.MutationApplied, 0, true)]
    [Arguments(CommitStage.ApplyCompleted, 0, true)]
    public async Task AcEventRetention004AdmittedPurgeRecoversOneWholeSuccessOrPinnedReceipt(CommitStage stage, int index, bool pinned)
        => await TopicPurgeCrashTrial.RunAsync(stage, index, pinned, TestContext.Current!.Execution.CancellationToken);
}
