using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.ProcessRecoveryTests.Messaging;

internal sealed class LocalInboxProcessingProcessRecoveryTests
{
    private const string TrialPrefix = "keyload-local-processing-crash-";
    private const int IntermediateMutation = 3;
    private const int LaterMutation = 6;

    [Test]
    [Arguments(CommitStage.HeaderWritten, 0)]
    [Arguments(CommitStage.PayloadWritten, 0)]
    [Arguments(CommitStage.JournalFlushed, 0)]
    [Arguments(CommitStage.MutationApplied, 0)]
    [Arguments(CommitStage.MutationApplied, IntermediateMutation)]
    [Arguments(CommitStage.MutationApplied, LaterMutation)]
    [Arguments(CommitStage.ApplyCompleted, 0)]
    public Task LocalQueueProcessingCrashRecoversOneEffectsInboxAckOutboxOutcomeCutThenReplayAndColdHealthyContinuation(CommitStage stage, int index)
        => MessagingCrashTrial.RunAsync(TrialPrefix, LocalInboxProcessingCrashContract.Mode, stage, index,
            LocalInboxProcessingProcessRecovery.VerifyAsync, TestContext.Current!.Execution.CancellationToken);
}
