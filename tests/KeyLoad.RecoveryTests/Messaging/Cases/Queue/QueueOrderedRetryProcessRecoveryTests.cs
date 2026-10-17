using KeyLoad.CrashHost;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal sealed class QueueOrderedRetryProcessRecoveryTests
{
    private const string TrialPrefix = "keyload-ordered-retry-crash-";

    [Test]
    [Arguments(CommitStage.HeaderWritten)]
    [Arguments(CommitStage.PayloadWritten)]
    [Arguments(CommitStage.JournalFlushed)]
    [Arguments(CommitStage.MutationApplied)]
    [Arguments(CommitStage.ApplyCompleted)]
    public Task StrictHeadSignedRetryProcessKillRetainsOneCompleteCutAndOriginalReceiptThroughHealthyColdContinuation(CommitStage stage)
        => MessagingCrashTrial.RunAsync(TrialPrefix, QueueOrderedRetryCrashProtocol.Mode, stage, QueueOrderedRetryCrashProtocol.Initial,
            QueueOrderedRetryRecovery.VerifyAsync, TestContext.Current!.Execution.CancellationToken);
}
