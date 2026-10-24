using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.EventStreams;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class StreamTraversalColdClusterTests(ClusterFixture fixture)
{
    [Test]
    public Task OriginalSignedForwardAndBackwardCutsExcludeLaterAppendAcrossPublicRoutesAndTrueCold()
        => StreamTraversalColdClusterFlow.RunAsync(fixture);
}
