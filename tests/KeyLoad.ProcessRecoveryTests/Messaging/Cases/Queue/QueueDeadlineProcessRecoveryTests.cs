using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.ProcessRecoveryTests.Messaging;

internal sealed class QueueDeadlineProcessRecoveryTests
{
    private const string TrialPrefix = "keyload-queue-deadline-crash-";
    private const int FirstMutation = 0;

    [Test]
    [Arguments(CommitStage.WriteAheadLogSynced)]
    [Arguments(CommitStage.MutationApplied)]
    [Arguments(CommitStage.ApplyCompleted)]
    public Task ExpiredReadyDeadlineProcessCutRecoversWholeBodyCountersReceiptThenSameRootHealthy(CommitStage stage)
        => MessagingCrashTrial.RunAsync(TrialPrefix, QueueDeadlineCrashProtocol.Mode, stage, FirstMutation,
            QueueDeadlineRecoveryAssertions.VerifyAsync, TestContext.Current!.Execution.CancellationToken);
}
