namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Actual original caller cancellation leaves Close charged until authorized native Abort joins it.</summary>
[NotInParallel]
internal sealed class PartitionMovementTransferCloseRf3Tests
{
    [Test]
    public Task ActualOpenThenOriginalCallerCancellationRecordsCloseFailureBeforeFreshAbortJoinsSourceAndColdSdkMcpQ1IsHealthy()
        => PartitionMovementTransferCloseRf3Trial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
