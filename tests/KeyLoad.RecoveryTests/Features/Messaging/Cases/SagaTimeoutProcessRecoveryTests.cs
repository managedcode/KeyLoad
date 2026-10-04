using KeyLoad.CrashHost;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal sealed class SagaTimeoutProcessRecoveryTests
{
    private const string TrialPrefix = "keyload-saga-timeout-crash-";
    private const int MutationIndex = 0;

    [Test]
    [Arguments(CommitStage.HeaderWritten)]
    [Arguments(CommitStage.PayloadWritten)]
    [Arguments(CommitStage.JournalFlushed)]
    [Arguments(CommitStage.MutationApplied)]
    [Arguments(CommitStage.ApplyCompleted)]
    public Task AcJobs003ProcessKillRecoversOneSagaTimeoutAndStableEnqueueRetry(CommitStage stage)
        => MessagingCrashTrial.RunAsync(TrialPrefix, SagaTimeoutCrashScenario.Mode, stage, MutationIndex,
            SagaTimeoutRecoveryAssertions.VerifyAsync, TestContext.Current!.Execution.CancellationToken);
}
