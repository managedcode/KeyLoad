using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class ComposedProjectionLineageRf3Tests(ClusterFixture fixture)
{
    [Test]
    public async Task AcKl048ComposedSourceProjectionAndPredicateUseRevokeRepairPreserveSdkMcpRowsAndReceiptsAcrossTwoColdCuts()
    {
        using var deadline = McpCallerDeadline.Create();
        var token = deadline.Token;
        var state = await ComposedProjectionLineageRf3Scenario.StartAsync(fixture, token);
        await ComposedProjectionLineageRf3Scenario.PolicyAsync(fixture, state, 3, true, true, false, token);
        await ComposedProjectionLineageRf3Flow.VerifyAsync(fixture, state, false, true, token);
        await ComposedProjectionLineageRf3Scenario.PolicyAsync(fixture, state, 4, true, false, true, token);
        await ComposedProjectionLineageRf3Flow.VerifyAsync(fixture, state, true, false, token);
        await ComposedProjectionLineageRf3Scenario.PolicyAsync(fixture, state, 5, true, true, true, token);
        await ComposedProjectionLineageRf3Flow.VerifyAsync(fixture, state, false, false, token);
        await ModelSqlRf3Cold.RunAsync(fixture, token);
        await ComposedProjectionLineageRf3Flow.VerifyAsync(fixture, state, false, false, token);
        await ComposedProjectionLineageRf3Scenario.PolicyAsync(fixture, state, 6, false, true, true, token);
        await ComposedProjectionLineageRf3Flow.VerifyAsync(fixture, state, true, false, token);
        await ComposedProjectionLineageRf3Scenario.PolicyAsync(fixture, state, 7, true, true, true, token);
        await ComposedProjectionLineageRf3Flow.VerifyAsync(fixture, state, false, false, token);
        await ComposedProjectionLineageRf3Scenario.PolicyAsync(fixture, state, 8, true, true, true, token, scoreRead: false);
        await ComposedProjectionLineageRf3Flow.VerifyAsync(fixture, state, false, false, token, scoreMasked: true);
        await ComposedProjectionLineageRf3Scenario.PolicyAsync(fixture, state, 9, true, true, true, token);
        await ComposedProjectionLineageRf3Flow.VerifyAsync(fixture, state, false, false, token);
        state = await ComposedProjectionLineageRf3Scenario.RepairAsync(fixture, state, token);
        await ComposedProjectionLineageRf3Flow.VerifyAsync(fixture, state, false, false, token);
        await ModelSqlRf3Cold.RunAsync(fixture, token);
        await ComposedProjectionLineageRf3Flow.VerifyAsync(fixture, state, false, false, token);
    }
}
