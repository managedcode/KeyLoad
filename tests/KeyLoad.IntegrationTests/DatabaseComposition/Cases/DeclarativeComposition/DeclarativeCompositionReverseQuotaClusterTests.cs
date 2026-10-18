using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class DeclarativeCompositionReverseQuotaClusterTests(ClusterFixture fixture)
{
    [Test]
    public async Task ReverseQueueQuotaRefusesAtomicallyThenRepairsAndReplaysLiteralModelsAcrossTwoColdCuts()
    {
        using var deadline = McpCallerDeadline.Create();
        var state = await RequestCqrsClusterCallers.RunOwnedAsync<DeclarativeReverseQuotaState>(fixture.App,
            McpCallerProtocol.Node1, fixture.AdminKey,
            callers => DeclarativeReverseQuotaFlow.SeedAndRefuseAsync(callers, deadline.Token), deadline.Token);
        await ModelSqlClusterCold.RunAsync(fixture, deadline.Token);
        state = await RequestCqrsClusterCallers.RunOwnedAsync<DeclarativeReverseQuotaState>(fixture.App,
            McpCallerProtocol.Node3, fixture.AdminKey,
            callers => DeclarativeReverseQuotaFlow.RepairAndHealthyAsync(callers, state, deadline.Token), deadline.Token);
        await ModelSqlClusterCold.RunAsync(fixture, deadline.Token);
        await RequestCqrsClusterCallers.RunOwnedAsync<bool>(fixture.App, McpCallerProtocol.Node2, fixture.AdminKey,
            async callers =>
            {
                await DeclarativeReverseQuotaFlow.OriginalAsync(callers, state, deadline.Token);
                return true;
            }, deadline.Token);
    }
}
