using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class ControlledPartitionMovementProcessRecoveryTests
{
    [Test]
    [Arguments(CommitStage.HeaderWritten)]
    [Arguments(CommitStage.PayloadWritten)]
    [Arguments(CommitStage.JournalFlushed)]
    [Arguments(CommitStage.MutationApplied)]
    [Arguments(CommitStage.ApplyCompleted)]
    public Task OriginalAdmittedFenceSurvivesFourOwnedChildrenWithMixedStateExactReplayAndHealthySettlement(CommitStage cut)
        => ControlledPartitionMovementProcessOperation.RunAsync(cut, RecoveryExecutionOptions.NativeProcessReadiness(),
            TimeProvider.System, TestContext.Current!.Execution.CancellationToken);
}
