namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class PlacementCapacityAdmissionTests
{
    [Test]
    public Task OriginalControlAdmissionBoundsCancellationAndJoinedShutdownPreserveHealthyContinuation()
        => PlacementCapacityAdmissionFlow.RunAsync();
}
