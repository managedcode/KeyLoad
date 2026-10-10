using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal sealed class SubscriptionFilterProcessRecoveryTests
{
    [Test]
    [Arguments(CommitStage.HeaderWritten, 0)]
    [Arguments(CommitStage.PayloadWritten, 0)]
    [Arguments(CommitStage.JournalFlushed, 0)]
    [Arguments(CommitStage.MutationApplied, 0)]
    [Arguments(CommitStage.MutationApplied, 1)]
    [Arguments(CommitStage.MutationApplied, 2)]
    [Arguments(CommitStage.ApplyCompleted, 0)]
    public Task FilterGenerationAndOldWindowRecoverAsOneNativeTransaction(CommitStage stage, int index)
        => SubscriptionFilterProcessTrial.RunAsync(stage, index, TestContext.Current!.Execution.CancellationToken);
}
