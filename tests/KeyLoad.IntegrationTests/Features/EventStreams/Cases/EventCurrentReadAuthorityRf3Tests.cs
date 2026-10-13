using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class EventCurrentReadAuthorityRf3Tests(ClusterFixture fixture)
{
    [Test]
    public async Task OnlyEventsReadRevocationFencesCapturedTraversalAcrossColdThenRepairKeepsLiteralSnapshotAndOriginalReceipts()
    {
        using var deadline = McpCallerDeadline.Create();
        await EventCurrentReadAuthorityRf3Flow.RunAsync(fixture, deadline.Token);
    }
}
