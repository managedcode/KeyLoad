namespace KeyLoad.UnitTests.ResourceExecution;

internal sealed class HeavyRouteAdmissionWholeFlowTests
{
    [Test]
    [Arguments("/search/graph")]
    [Arguments(AnnSearchProtocol.Route)]
    [Arguments("/graph/shortest-path")]
    [Arguments("/graph/incoming")]
    public Task HeavyReservationRefusalReleaseFreshPersistedGrantRepairReadReplayAndTwoCold(string route)
        => HeavyRouteAdmissionFlow.RunAsync(route, TestContext.Current!.Execution.CancellationToken);
}
