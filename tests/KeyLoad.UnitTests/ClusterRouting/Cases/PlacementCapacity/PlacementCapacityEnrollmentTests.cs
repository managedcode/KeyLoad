namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class PlacementCapacityEnrollmentTests
{
    [Test]
    public Task OriginalPersistedEnrollmentFencesOwnerTupleAcrossRepairReplayAndTwoColdCuts()
        => PlacementCapacityEnrollmentFlow.RunAsync();
}
