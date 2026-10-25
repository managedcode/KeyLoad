using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.EventStreams;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class EventSelectiveAppendAuthorityClusterTests(ClusterFixture fixture)
{
    [Test]
    public async Task OnlyEventsAppendRevocationPreservesReadsAndFencesOriginalReceiptThenFreshAppendSurvivesTwoColdOwners()
    {
        using var deadline = McpCallerDeadline.Create();
        await EventSelectiveAppendAuthorityClusterFlow.RunAsync(fixture, deadline.Token);
    }
}
