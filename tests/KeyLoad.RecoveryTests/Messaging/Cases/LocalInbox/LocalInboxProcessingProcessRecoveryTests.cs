using KeyLoad.CrashHost.Features.Messaging;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal sealed class LocalInboxProcessingProcessRecoveryTests
{
    private const string TrialPrefix = "keyload-local-processing-crash-";
    private const int InitialMutation = 0;
    private const int IntermediateMutation = 3;
    private const int LaterMutation = 6;

    [Test]
    [Arguments(CommitStage.HeaderWritten, InitialMutation)]
    [Arguments(CommitStage.PayloadWritten, InitialMutation)]
    [Arguments(CommitStage.JournalFlushed, InitialMutation)]
    [Arguments(CommitStage.MutationApplied, InitialMutation)]
    [Arguments(CommitStage.MutationApplied, IntermediateMutation)]
    [Arguments(CommitStage.MutationApplied, LaterMutation)]
    [Arguments(CommitStage.ApplyCompleted, InitialMutation)]
    public Task LocalQueueProcessingCrashRecoversOneEffectsInboxAckOutboxOutcomeCutThenReplayAndColdHealthyContinuation(CommitStage stage, int index)
        => MessagingCrashTrial.RunAsync(TrialPrefix, LocalInboxProcessingCrashContract.Mode, stage, index,
            LocalInboxProcessingProcessRecovery.VerifyAsync, TestContext.Current!.Execution.CancellationToken);
}
