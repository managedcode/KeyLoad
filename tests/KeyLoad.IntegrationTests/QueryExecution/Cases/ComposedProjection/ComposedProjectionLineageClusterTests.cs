using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class ComposedProjectionLineageClusterTests(ClusterFixture fixture)
{
    [Test]
    public async Task ComposedSourceProjectionAndPredicateUseRevokeRepairPreserveSdkMcpRowsAndReceiptsAcrossTwoColdCuts()
    {
        using var deadline = McpCallerDeadline.Create();
        var token = deadline.Token;
        var state = await ComposedProjectionLineageClusterScenario.StartAsync(fixture, token);
        await ComposedProjectionLineageClusterScenario.PolicyAsync(fixture, state, 3, true, true, false, token);
        await ComposedProjectionLineageClusterFlow.VerifyAsync(fixture, state, false, true, token);
        await ComposedProjectionLineageClusterScenario.PolicyAsync(fixture, state, 4, true, false, true, token);
        await ComposedProjectionLineageClusterFlow.VerifyAsync(fixture, state, true, false, token);
        await ComposedProjectionLineageClusterScenario.PolicyAsync(fixture, state, 5, true, true, true, token);
        await ComposedProjectionLineageClusterFlow.VerifyAsync(fixture, state, false, false, token);
        await ModelSqlClusterCold.RunAsync(fixture, token);
        await ComposedProjectionLineageClusterFlow.VerifyAsync(fixture, state, false, false, token);
        await ComposedProjectionLineageClusterScenario.PolicyAsync(fixture, state, 6, false, true, true, token);
        await ComposedProjectionLineageClusterFlow.VerifyAsync(fixture, state, true, false, token);
        await ComposedProjectionLineageClusterScenario.PolicyAsync(fixture, state, 7, true, true, true, token);
        await ComposedProjectionLineageClusterFlow.VerifyAsync(fixture, state, false, false, token);
        await ComposedProjectionLineageClusterScenario.PolicyAsync(fixture, state, 8, true, true, true, token, scoreRead: false);
        await ComposedProjectionLineageClusterFlow.VerifyAsync(fixture, state, false, false, token, scoreMasked: true);
        await ComposedProjectionLineageClusterScenario.PolicyAsync(fixture, state, 9, true, true, true, token);
        await ComposedProjectionLineageClusterFlow.VerifyAsync(fixture, state, false, false, token);
        state = await ComposedProjectionLineageClusterScenario.RepairAsync(fixture, state, token);
        await ComposedProjectionLineageClusterFlow.VerifyAsync(fixture, state, false, false, token);
        await ModelSqlClusterCold.RunAsync(fixture, token);
        await ComposedProjectionLineageClusterFlow.VerifyAsync(fixture, state, false, false, token);
    }
}
