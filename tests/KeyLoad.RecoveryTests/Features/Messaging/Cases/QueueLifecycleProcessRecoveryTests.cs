using KeyLoad.CrashHost;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal sealed class QueueLifecycleProcessRecoveryTests
{
    private const string TrialPrefix = "keyload-queue-lifecycle-crash-";
    private const int FirstMutation = 0;

    [Test]
    [Arguments(CommitStage.HeaderWritten)]
    [Arguments(CommitStage.PayloadWritten)]
    [Arguments(CommitStage.JournalFlushed)]
    [Arguments(CommitStage.MutationApplied)]
    [Arguments(CommitStage.ApplyCompleted)]
    public Task CancelParkRedriveProcessKillHasOneCompleteNativeCutAndSameIdHealthyContinuation(CommitStage stage)
        => MessagingCrashTrial.RunAsync(TrialPrefix, QueueLifecycleCrashProtocol.Mode, stage, FirstMutation,
            QueueLifecycleRecoveryAssertions.VerifyAsync, TestContext.Current!.Execution.CancellationToken);
}
