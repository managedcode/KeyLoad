using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.Authorization;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

/// <summary>AC-PQUERY-002 fails closed when a later canonical leaf lacks persisted read authority.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class PartitionQueryPublicAuthorizationTests(ClusterFixture fixture)
{
    [Test]
    public async Task AcPquery002LaterPersistedLeafDenialReturnsNoSdkOrMcpPageAndLeavesNextReadHealthy()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await PartitionQueryRf3Scenario.CreateAuthorizationAsync(fixture, deadline.Token)
            .ConfigureAwait(false);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partitions[0],
            PartitionQueryRf3Protocol.Collection, Capability.DocumentsRead | Capability.Query, deadline.Token)
            .ConfigureAwait(false);
        var state = await PartitionAuthorityFlow.WarmAsync(fixture, scenario, identity,
            () => VerifyHealthyReadAsync(fixture, scenario, deadline.Token), deadline.Token);
        await PartitionAuthorityCold.RunAsync(fixture, state, deadline.Token);
    }

    private static async Task VerifyHealthyReadAsync(ClusterFixture fixture,
        PartitionQueryRf3Scenario scenario, CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node3);
        var administrator = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var request = scenario.Request([scenario.Partitions[0]]);
        var result = await McpCallerAssertions.SdkSuccessAsync(await administrator.PartitionQueryAsync(request,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await PartitionQueryRf3Assertions.AssertOracleAsync(result!, scenario, request.Partitions).ConfigureAwait(false);
    }
}
