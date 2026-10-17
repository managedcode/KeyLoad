using KeyLoad.CrashHost;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal sealed class QueueDeadlineProcessRecoveryTests
{
    private const string TrialPrefix = "keyload-queue-deadline-crash-";
    private const int FirstMutation = 0;

    [Test]
    [Arguments(CommitStage.HeaderWritten)]
    [Arguments(CommitStage.PayloadWritten)]
    [Arguments(CommitStage.JournalFlushed)]
    [Arguments(CommitStage.MutationApplied)]
    [Arguments(CommitStage.ApplyCompleted)]
    public Task ExpiredReadyDeadlineProcessCutRecoversWholeBodyCountersReceiptThenSameRootHealthy(CommitStage stage)
        => MessagingCrashTrial.RunAsync(TrialPrefix, QueueDeadlineCrashProtocol.Mode, stage, FirstMutation,
            QueueDeadlineRecoveryAssertions.VerifyAsync, TestContext.Current!.Execution.CancellationToken);
}
