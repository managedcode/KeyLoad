using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class StreamTraversalColdRf3Tests(ClusterFixture fixture)
{
    [Test]
    public Task OriginalSignedForwardAndBackwardCutsExcludeLaterAppendAcrossPublicRoutesAndTrueCold()
        => StreamTraversalColdRf3Flow.RunAsync(fixture);
}
