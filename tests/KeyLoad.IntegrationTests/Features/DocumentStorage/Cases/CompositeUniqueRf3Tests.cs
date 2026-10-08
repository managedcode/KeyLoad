using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class CompositeUniqueRf3Tests(ClusterFixture fixture)
{
    [Test]
    public Task Kl011CompositeUniqueDeltasRollbackScopeAndPersistedGrantRestorationMatchBothClients()
        => CompositeUniqueRf3Flow.RunAsync(fixture);
}
