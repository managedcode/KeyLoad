using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateNodeAssertions
{
    internal static async Task VerifyAsync(FunctionalGateFixture fixture, FunctionalGateContext context,
        CancellationToken token)
    {
        var owners = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in fixture.NodeNames)
        {
            var status = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, node, fixture.AdminKey, async caller =>
            {
                var sdk = await context.SdkAsync(caller.Sdk.StatusAsync(token));
                var mcp = await context.McpWithoutBodyAsync<NodeStatus>(caller, McpCallerTools.AdminStatus, token);
                await Assert.That(mcp.NodeId).IsEqualTo(sdk.NodeId);
                await Assert.That(mcp.Incarnation).IsEqualTo(sdk.Incarnation);
                await RequireAsync(sdk, fixture.NodeNames.Length, fixture.ReplicaCount);
                await RequireAsync(mcp, fixture.NodeNames.Length, fixture.ReplicaCount);
                return sdk;
            }, token);
            await Assert.That(owners.Add(status.NodeId)).IsTrue();
        }
        await Assert.That(owners.Count).IsEqualTo(fixture.NodeNames.Length);
    }

    private static async Task RequireAsync(NodeStatus status, int silos, int replicas)
    {
        await Assert.That(Guid.TryParse(status.NodeId, out var owner) && owner != Guid.Empty).IsTrue();
        await Assert.That(status.Incarnation).IsNotEqualTo(Guid.Empty);
        await Assert.That(status.SiloCount).IsEqualTo(silos);
        await Assert.That(status.ReplicaCount).IsEqualTo(replicas);
        await Assert.That(status.RoutingReady).IsTrue();
        await Assert.That(status.ProcessId).IsGreaterThan(0);
        await Assert.That(status.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
    }
}
