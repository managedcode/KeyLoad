using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class TypedEndpointJoinRf3Tests(ClusterFixture fixture)
{
    [Test]
    public async Task AcKl095TypedEndpointInnerJoinRefusesPolicyAndPartialFailureThenReplaysLiteralModelsAcrossTwoColdCuts()
    {
        using var deadline = McpCallerDeadline.Create();
        var seed = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async admin => await TypedEndpointJoinSetup.RunAsync(fixture, admin, deadline.Token), deadline.Token);
        var state = await TypedEndpointJoinAuthority.WarmAsync(fixture, seed, deadline.Token);
        await TypedEndpointJoinColdFlow.RunAsync(fixture, state, deadline.Token);
    }
}
