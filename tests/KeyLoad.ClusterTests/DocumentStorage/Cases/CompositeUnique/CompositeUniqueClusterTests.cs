using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.DocumentStorage;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class CompositeUniqueClusterTests(ClusterFixture fixture)
{
    [Test]
    public Task CompositeUniqueDeltasRollbackScopeAndPersistedGrantRestorationMatchBothClients()
        => CompositeUniqueClusterFlow.RunAsync(fixture);
}
