using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateColdNodeObservations
{
    internal static async Task<FunctionalGateColdState> CaptureAsync(FunctionalGateFixture fixture,
        FunctionalGateColdState state, CancellationToken token)
    {
        var snapshots = new List<AdminNodeSnapshot>();
        foreach (var node in fixture.NodeNames)
        {
            snapshots.Add(await ReadAsync(fixture, node, token));
        }
        return state with { Nodes = [.. snapshots] };
    }

    internal static async Task VerifyAsync(FunctionalGateFixture fixture, FunctionalGateColdState state, CancellationToken token)
    {
        await Assert.That(state.Nodes.Length).IsEqualTo(fixture.NodeNames.Length);
        for (var index = 0; index < fixture.NodeNames.Length; index++)
        {
            var before = state.Nodes[index];
            var after = await ReadAsync(fixture, fixture.NodeNames[index], token);
            await Assert.That(after.Node.NodeId).IsEqualTo(before.Node.NodeId);
            await Assert.That(after.Node.Incarnation).IsEqualTo(before.Node.Incarnation);
            await Assert.That(after.Http.ProcessInstance).IsNotEqualTo(before.Http.ProcessInstance);
            await Assert.That(after.Node.Position).IsGreaterThanOrEqualTo(before.Node.Position);
            await Assert.That(after.SiloName).IsEqualTo(before.SiloName);
        }
    }

    private static Task<AdminNodeSnapshot> ReadAsync(FunctionalGateFixture fixture, string node, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, node, fixture.AdminKey, async administrator =>
        {
            var sdk = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.DashboardAsync(token));
            var official = await McpCallerAssertions.SuccessAsync<AdminNodeSnapshot>(
                await administrator.Mcp.CallWithoutBodyAsync(AdminDashboardProtocol.SnapshotTool, token));
            foreach (var snapshot in new[] { sdk, official.Value })
            {
                await Assert.That(snapshot.Node.SiloCount).IsEqualTo(fixture.NodeNames.Length);
                await Assert.That(snapshot.Node.ReplicaCount).IsEqualTo(fixture.NodeNames.Length);
                await Assert.That(snapshot.Node.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
                await Assert.That(snapshot.Node.RoutingReady).IsTrue();
                await Assert.That(snapshot.SiloName).IsEqualTo(node);
                await Assert.That(snapshot.ActiveSilos.ToHashSet(StringComparer.Ordinal).SetEquals(fixture.NodeNames)).IsTrue();
                await Assert.That(snapshot.Node.ProcessId).IsGreaterThan(0);
                await Assert.That(snapshot.Http.ProcessInstance).IsNotEqualTo(Guid.Empty);
            }
            await Assert.That(official.Value.Node.NodeId).IsEqualTo(sdk.Node.NodeId);
            await Assert.That(official.Value.Node.Incarnation).IsEqualTo(sdk.Node.Incarnation);
            await Assert.That(official.Value.Http.ProcessInstance).IsEqualTo(sdk.Http.ProcessInstance);
            return official.Value;
        }, token);
}
