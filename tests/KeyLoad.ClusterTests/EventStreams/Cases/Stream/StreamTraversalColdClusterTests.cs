using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.EventStreams;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class StreamTraversalColdClusterTests(ClusterFixture fixture)
{
    [Test]
    public Task OriginalSignedForwardAndBackwardCutsExcludeLaterAppendAcrossPublicRoutesAndTrueCold()
        => StreamTraversalColdClusterFlow.RunAsync(fixture);
}
