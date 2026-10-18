namespace KeyLoad.UnitTests.Features.ResourceExecution;

[NotInParallel]
internal sealed class NativeDropFailureTests
{
    [Test]
    public Task BoundedDropFailureReplaysOriginalOutcomeAndHealthyOperationsSurviveTwoColdReopens()
        => NativeDropOperationFlow.RunAsync(NativeDropBudgetFailure.ExecuteAsync,
            new() { MaxScanRecords = NativeDropRuntimeProtocol.TinyScanRecords });

    [Test]
    public Task CommittedDropReplyFailureReplaysOriginalSuccessAcrossTwoJoinedColdReopens()
        => NativeDropOperationFlow.RunAsync(NativeDropReplyFailure.ExecuteAsync);

    [Test]
    public Task CancelledDropBeforeSubmitCannotPublishLateAndOriginalCommandRetriesAcrossTwoColdReopens()
        => NativeDropOperationFlow.RunAsync(NativeDropCancellation.ExecuteAsync);
}
