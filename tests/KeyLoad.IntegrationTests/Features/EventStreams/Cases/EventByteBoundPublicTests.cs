using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class EventByteBoundPublicTests(ClusterFixture fixture)
{
    [Test]
    public async Task OneByteWholeEventRefusalPreservesSignedTraversalAndOriginalReceiptsAcrossTwoColdOwners()
    {
        using var deadline = McpCallerDeadline.Create();
        await EventByteBoundPublicFlow.RunAsync(fixture, deadline.Token);
    }
}
