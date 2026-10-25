using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.EventStreams;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class EventCurrentReadAuthorityClusterTests(ClusterFixture fixture)
{
    [Test]
    public async Task OnlyEventsReadRevocationFencesCapturedTraversalAcrossColdThenRepairKeepsLiteralSnapshotAndOriginalReceipts()
    {
        using var deadline = McpCallerDeadline.Create();
        await EventCurrentReadAuthorityClusterFlow.RunAsync(fixture, deadline.Token);
    }
}
