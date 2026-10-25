using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;

namespace KeyLoad.ClusterTests.DatabaseComposition;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class TypedEndpointJoinClusterTests(ClusterFixture fixture)
{
    [Test]
    public async Task TypedEndpointInnerJoinRefusesPolicyAndPartialFailureThenReplaysLiteralModelsAcrossTwoColdCuts()
    {
        using var deadline = McpCallerDeadline.Create();
        var seed = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async admin => await TypedEndpointJoinSetup.RunAsync(fixture, admin, deadline.Token), deadline.Token);
        var state = await TypedEndpointJoinAuthority.WarmAsync(fixture, seed, deadline.Token);
        await TypedEndpointJoinColdFlow.RunAsync(fixture, state, deadline.Token);
    }
}
