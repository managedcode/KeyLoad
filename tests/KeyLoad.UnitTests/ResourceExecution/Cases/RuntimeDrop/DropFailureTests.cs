namespace KeyLoad.UnitTests.Features.ResourceExecution;

[NotInParallel]
internal sealed class DropFailureTests
{
    [Test]
    public Task BoundedDropFailureReplaysOriginalOutcomeAndHealthyOperationsSurviveTwoColdReopens()
        => DropOperationFlow.RunAsync(DropBudgetFailure.ExecuteAsync,
            new() { MaxScanRecords = DropRuntimeProtocol.TinyScanRecords });

    [Test]
    public Task CommittedDropReplyFailureReplaysOriginalSuccessAcrossTwoJoinedColdReopens()
        => DropOperationFlow.RunAsync(DropReplyFailure.ExecuteAsync);

    [Test]
    public Task CancelledDropBeforeSubmitCannotPublishLateAndOriginalCommandRetriesAcrossTwoColdReopens()
        => DropOperationFlow.RunAsync(DropCancellation.ExecuteAsync);
}
