namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Real committed Capture authority rejects each stopped Header19 fault before forward effects.</summary>
[NotInParallel]
internal sealed class PartitionMovementCapturePointerRf3Tests
{
    [Test]
    public Task MissingOriginalCapturePointerRefusesThenExactOriginalColdResumeIsHealthy()
        => PartitionMovementCapturePointerRf3Trial.RunAsync(PartitionMovementCapturePointerFault.Missing,
            TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task ChangedOriginalCapturePointerRefusesThenExactOriginalColdResumeIsHealthy()
        => PartitionMovementCapturePointerRf3Trial.RunAsync(PartitionMovementCapturePointerFault.AbsentRow,
            TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task ActualNonCaptureRowPointerRefusesThenExactOriginalColdResumeIsHealthy()
        => PartitionMovementCapturePointerRf3Trial.RunAsync(PartitionMovementCapturePointerFault.NonCaptureRow,
            TestContext.Current!.Execution.CancellationToken);
}
