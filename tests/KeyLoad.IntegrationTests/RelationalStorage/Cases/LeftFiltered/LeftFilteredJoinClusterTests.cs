using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class LeftFilteredJoinClusterTests(ClusterFixture fixture)
{
    private const long GrantedPolicyEpoch = 3;
    private const long RevokedPolicyEpoch = 4;
    private const long RepairedPolicyEpoch = 5;

    [Test]
    public async Task LeftFilteredJoinRepairsPersistedUseAndReturnsLiteralSdkMcpPagesAcrossTwoColdCuts()
    {
        using var deadline = McpCallerDeadline.Create();
        var token = deadline.Token;
        var state = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, callers => LeftFilteredJoinSetup.RunAsync(fixture, callers.Sdk, token), token);
        await LeftFilteredJoinPublicFlow.DeniedAsync(fixture, state, token);
        await RepairAsync(state, GrantedPolicyEpoch, allow: true, readName: false, token);
        await LeftFilteredJoinPublicFlow.HealthyAsync(fixture, state, masked: true, token);
        await ModelSqlClusterCold.RunAsync(fixture, token);
        await LeftFilteredJoinPublicFlow.HealthyAsync(fixture, state, masked: true, token);
        await RepairAsync(state, RevokedPolicyEpoch, allow: false, readName: false, token);
        await LeftFilteredJoinPublicFlow.DeniedAsync(fixture, state, token);
        await RepairAsync(state, RepairedPolicyEpoch, allow: true, readName: true, token);
        await LeftFilteredJoinPublicFlow.HealthyAsync(fixture, state, masked: false, token);
        await ModelSqlClusterCold.RunAsync(fixture, token);
        await LeftFilteredJoinPublicFlow.HealthyAsync(fixture, state, masked: false, token);
    }

    private async Task RepairAsync(LeftFilteredJoinState state, long epoch, bool allow, bool readName,
        CancellationToken token)
        => await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async callers =>
            {
                await LeftFilteredJoinSetup.RepairAsync(callers.Sdk, state, epoch, allow, readName, token);
                return true;
            }, token);
}
