using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;
using P = KeyLoad.ClusterTests.ClientApi.McpAnalyticsDropDiscoveryProtocol;

namespace KeyLoad.ClusterTests.ClientApi;

internal static class McpAnalyticsDropDiscoveryAuthorization
{
    internal static async Task VerifyAsync(ClusterFixture fixture, RequestCqrsClusterCallers root,
        McpAnalyticsDropDiscoveryState state, CancellationToken token)
    {
        var identity = await McpPersistedIdentity.CreateAsync(root.Sdk, state.Partition, "*",
            Capability.AnalyticsRead | Capability.DocumentsRead, token);
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, identity.Secret, async reader =>
        {
            await McpAnalyticsDropDiscoveryAssertions.ReadsAsync(reader, state, token);
            var revoked = identity.Principal with
            {
                Grants = [],
                PolicyEpoch = checked(identity.Principal.PolicyEpoch + 1)
            };
            await SqlClusterProtocol.EqualAsync(revoked, await McpCallerAssertions.SdkSuccessAsync(
                await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), revoked, token)));
            await DeniedAsync(reader, state, token);
            await McpAnalyticsDropDiscoveryAssertions.ReadsAsync(root, state, token);
            var repaired = identity.Principal with
            {
                PolicyEpoch = checked(revoked.PolicyEpoch + 1)
            };
            await SqlClusterProtocol.EqualAsync(repaired, await McpCallerAssertions.SdkSuccessAsync(
                await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), repaired, token)));
            await McpAnalyticsDropDiscoveryAssertions.ReadsAsync(reader, state, token);
            return true;
        }, token);
    }

    private static async Task DeniedAsync(RequestCqrsClusterCallers reader,
        McpAnalyticsDropDiscoveryState state, CancellationToken token)
    {
        var read = await reader.Sdk.ReadAnalyticsEventsAsync(P.Read(state.Partition), token);
        await Assert.That(read.IsFailed).IsTrue();
        await Assert.That(read.Value).IsNull();
        await Assert.That(read.Problem?.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
        _ = (await McpCallerAssertions.ErrorAsync(await reader.Mcp.CallAsync(AnalyticsStoreProtocol.ReadTool,
            P.Read(state.Partition), token), ErrorCode.PermissionDenied, dispatched: true)).Value;
        var aggregate = await reader.Sdk.AggregateAnalyticsAsync(P.Query(state.Partition), token);
        await Assert.That(aggregate.IsFailed).IsTrue();
        await Assert.That(aggregate.Value).IsNull();
        await Assert.That(aggregate.Problem?.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
        _ = (await McpCallerAssertions.ErrorAsync(await reader.Mcp.CallAsync(AnalyticsStoreProtocol.AggregateTool,
            P.Query(state.Partition), token), ErrorCode.PermissionDenied, dispatched: true)).Value;
    }
}
