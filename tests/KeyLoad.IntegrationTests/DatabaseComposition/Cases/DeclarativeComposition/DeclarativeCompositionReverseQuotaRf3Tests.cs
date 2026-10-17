using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class DeclarativeCompositionReverseQuotaRf3Tests(ClusterFixture fixture)
{
    [Test]
    public async Task AcKl095ReverseQueueQuotaRefusesAtomicallyThenRepairsAndReplaysLiteralModelsAcrossTwoColdCuts()
    {
        using var deadline = McpCallerDeadline.Create();
        var state = await RequestCqrsRf3Callers.RunOwnedAsync<DeclarativeReverseQuotaState>(fixture.App,
            McpCallerProtocol.Node1, fixture.AdminKey,
            callers => DeclarativeReverseQuotaFlow.SeedAndRefuseAsync(callers, deadline.Token), deadline.Token);
        await ModelSqlRf3Cold.RunAsync(fixture, deadline.Token);
        state = await RequestCqrsRf3Callers.RunOwnedAsync<DeclarativeReverseQuotaState>(fixture.App,
            McpCallerProtocol.Node3, fixture.AdminKey,
            callers => DeclarativeReverseQuotaFlow.RepairAndHealthyAsync(callers, state, deadline.Token), deadline.Token);
        await ModelSqlRf3Cold.RunAsync(fixture, deadline.Token);
        await RequestCqrsRf3Callers.RunOwnedAsync<bool>(fixture.App, McpCallerProtocol.Node2, fixture.AdminKey,
            async callers =>
            {
                await DeclarativeReverseQuotaFlow.OriginalAsync(callers, state, deadline.Token);
                return true;
            }, deadline.Token);
    }
}
