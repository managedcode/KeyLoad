using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class EventSelectiveAppendAuthorityRf3Tests(ClusterFixture fixture)
{
    [Test]
    public async Task OnlyEventsAppendRevocationPreservesReadsAndFencesOriginalReceiptThenFreshAppendSurvivesTwoColdOwners()
    {
        using var deadline = McpCallerDeadline.Create();
        await EventSelectiveAppendAuthorityRf3Flow.RunAsync(fixture, deadline.Token);
    }
}
