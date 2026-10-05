using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

/// <summary>AC-PQUERY-006 caller parity on explicit, fallback and empty leaves sharing one RF3 owner.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class PartitionQueryPublicRf3Tests(ClusterFixture fixture)
{
    [Test]
    public async Task AcPquery006SdkAndOfficialMcpReturnIndependentFullReferenceOrder()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await PartitionQueryRf3Scenario.CreateAsync(fixture, deadline.Token).ConfigureAwait(false);
        var request = scenario.Request();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var sdkPage = await McpCallerAssertions.SdkSuccessAsync(await sdk.PartitionQueryAsync(request, deadline.Token)
            .ConfigureAwait(false)).ConfigureAwait(false);
        await PartitionQueryRf3Assertions.AssertOracleAsync(sdkPage!, scenario, scenario.Partitions)
            .ConfigureAwait(false);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            fixture.AdminKey, deadline.Token).ConfigureAwait(false);
        var receipt = await McpCallerAssertions.SuccessAsync<PartitionQueryPageV1>(await mcp.CallAsync(
            PartitionQueryRf3Protocol.ToolName, request, deadline.Token).ConfigureAwait(false)).ConfigureAwait(false);
        await PartitionQueryRf3Assertions.AssertOracleAsync(receipt.Value, scenario, scenario.Partitions)
            .ConfigureAwait(false);
        await PartitionQueryRf3Assertions.AssertSameLogicalRowsAsync(sdkPage!, receipt.Value).ConfigureAwait(false);
    }
}
