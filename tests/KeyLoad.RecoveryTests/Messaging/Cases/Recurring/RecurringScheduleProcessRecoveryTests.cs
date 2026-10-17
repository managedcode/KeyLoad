using KeyLoad.CrashHost;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal sealed class RecurringScheduleProcessRecoveryTests
{
    private const string TrialPrefix = "keyload-recurring-schedule-crash-";
    private const int MutationIndex = 0;

    [Test]
    [Arguments(CommitStage.HeaderWritten)]
    [Arguments(CommitStage.PayloadWritten)]
    [Arguments(CommitStage.JournalFlushed)]
    [Arguments(CommitStage.MutationApplied)]
    [Arguments(CommitStage.ApplyCompleted)]
    public Task AcJobs001002ProcessKillRecoversOneScheduleWatermarkAndStableOccurrenceRetry(CommitStage stage)
        => MessagingCrashTrial.RunAsync(TrialPrefix, RecurringScheduleCrashScenario.Mode, stage, MutationIndex,
            RecurringScheduleRecoveryAssertions.VerifyAsync, TestContext.Current!.Execution.CancellationToken);
}
