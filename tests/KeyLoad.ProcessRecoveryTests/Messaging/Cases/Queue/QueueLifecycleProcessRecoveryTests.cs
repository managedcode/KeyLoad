using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.ProcessRecoveryTests.Messaging;

internal sealed class QueueLifecycleProcessRecoveryTests
{
    private const string TrialPrefix = "keyload-queue-lifecycle-crash-";
    private const int FirstMutation = 0;

    [Test]
    [Arguments(CommitStage.WriteAheadLogSynced)]
    [Arguments(CommitStage.MutationApplied)]
    [Arguments(CommitStage.ApplyCompleted)]
    public Task CancelParkRedriveProcessKillHasOneCompleteCutAndSameIdHealthyContinuation(CommitStage stage)
        => MessagingCrashTrial.RunAsync(TrialPrefix, QueueLifecycleCrashProtocol.Mode, stage, FirstMutation,
            QueueLifecycleRecoveryAssertions.VerifyAsync, TestContext.Current!.Execution.CancellationToken);
}
