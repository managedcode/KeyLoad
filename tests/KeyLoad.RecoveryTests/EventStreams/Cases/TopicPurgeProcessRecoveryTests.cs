using KeyLoad.Storage.ZoneTree;
namespace KeyLoad.RecoveryTests.Features.EventStreams;

internal sealed class TopicPurgeProcessRecoveryTests
{
    [Test]
    [Arguments(CommitStage.HeaderWritten, 0, false)]
    [Arguments(CommitStage.PayloadWritten, 0, false)]
    [Arguments(CommitStage.JournalFlushed, 0, false)]
    [Arguments(CommitStage.MutationApplied, 0, false)]
    [Arguments(CommitStage.ApplyCompleted, 0, false)]
    [Arguments(CommitStage.HeaderWritten, 0, true)]
    [Arguments(CommitStage.PayloadWritten, 0, true)]
    [Arguments(CommitStage.JournalFlushed, 0, true)]
    [Arguments(CommitStage.MutationApplied, 0, true)]
    [Arguments(CommitStage.ApplyCompleted, 0, true)]
    public async Task AcEventRetention004AdmittedPurgeRecoversOneWholeSuccessOrPinnedReceipt(CommitStage stage, int index, bool pinned)
        => await TopicPurgeCrashTrial.RunAsync(stage, index, pinned, TestContext.Current!.Execution.CancellationToken);
}
