using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.ProcessRecoveryTests.Messaging;

internal sealed class RecurringScheduleProcessRecoveryTests
{
    private const string TrialPrefix = "keyload-recurring-schedule-crash-";
    private const int MutationIndex = 0;

    [Test]
    [Arguments(CommitStage.WriteAheadLogSynced)]
    [Arguments(CommitStage.MutationApplied)]
    [Arguments(CommitStage.ApplyCompleted)]
    public Task AcJobs001002ProcessKillRecoversOneScheduleWatermarkAndStableOccurrenceRetry(CommitStage stage)
        => MessagingCrashTrial.RunAsync(TrialPrefix, RecurringScheduleCrashScenario.Mode, stage, MutationIndex,
            RecurringScheduleRecoveryAssertions.VerifyAsync, TestContext.Current!.Execution.CancellationToken);
}
