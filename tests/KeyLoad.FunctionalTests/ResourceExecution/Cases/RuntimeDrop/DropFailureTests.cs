namespace KeyLoad.FunctionalTests.ResourceExecution;

[NotInParallel]
internal sealed class DropFailureTests
{
    [Test]
    public Task CommittedDropReplyFailureReplaysOriginalSuccessAcrossTwoJoinedColdReopens()
        => DropOperationFlow.RunAsync(DropReplyFailure.ExecuteAsync);

    [Test]
    public Task CancelledCallerBeforeSubmitRetriesOriginalDropAfterReleaseAcrossTwoColdReopens()
        => DropOperationFlow.RunAsync(DropCancellation.ExecuteAsync);
}
