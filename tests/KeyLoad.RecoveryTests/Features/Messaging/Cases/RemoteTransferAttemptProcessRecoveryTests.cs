using KeyLoad.CrashHost.Features.Messaging;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal sealed class RemoteTransferAttemptProcessRecoveryTests
{
    private const string TrialPrefix = "keyload-transfer-attempt-crash-";
    private const int FirstMutation = 0;

    [Test]
    [Arguments(CommitStage.HeaderWritten)]
    [Arguments(CommitStage.JournalFlushed)]
    [Arguments(CommitStage.ApplyCompleted)]
    public Task ActualSourceAttemptCrashRetainsAtomicHistoryFailureAndCompletesOriginalTransferThenCold(CommitStage stage)
        => MessagingCrashTrial.RunAsync(TrialPrefix, RemoteTransferAttemptCrashProtocol.Mode, stage, FirstMutation,
            RemoteTransferAttemptProcessRecovery.VerifyAsync, TestContext.Current!.Execution.CancellationToken);
}
