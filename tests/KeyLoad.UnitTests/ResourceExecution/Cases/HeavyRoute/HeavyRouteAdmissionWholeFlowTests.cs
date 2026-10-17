namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class HeavyRouteAdmissionWholeFlowTests
{
    [Test]
    [Arguments("/v1/search/graph")]
    [Arguments(AnnSearchProtocol.Route)]
    [Arguments("/v1/graph/shortest-path")]
    [Arguments("/v1/graph/incoming")]
    public Task NativeHeavyReservationRefusalReleaseFreshPersistedGrantRepairReadReplayAndTwoCold(string route)
        => HeavyRouteAdmissionFlow.RunAsync(route, TestContext.Current!.Execution.CancellationToken);
}
