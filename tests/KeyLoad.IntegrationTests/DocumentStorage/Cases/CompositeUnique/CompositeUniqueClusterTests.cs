using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.DocumentStorage;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class CompositeUniqueClusterTests(ClusterFixture fixture)
{
    [Test]
    public Task CompositeUniqueDeltasRollbackScopeAndPersistedGrantRestorationMatchBothClients()
        => CompositeUniqueClusterFlow.RunAsync(fixture);
}
