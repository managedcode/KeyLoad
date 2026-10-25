using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.StorageReplication;

namespace KeyLoad.ClusterTests.Messaging;

internal sealed class QueueDeadlineSiloClusterOwnership(QueueDeadlineSiloClusterObservation original,
    string[] survivors)
{
    internal string OriginalSiloNode => original.ResourceName;
    internal string SurvivorNode => survivors[0];

    internal static async Task<QueueDeadlineSiloClusterOwnership> CaptureAsync(ClusterFixture fixture,
        CancellationToken token)
    {
        var captured = new List<QueueDeadlineSiloClusterObservation>();
        foreach (var name in QueueLeaseClusterProtocol.Nodes)
        {
            var observation = await ReadAsync(fixture, name, token);
            await Assert.That(observation.Status.SiloCount).IsEqualTo(QueueLeaseClusterProtocol.Nodes.Count);
            captured.Add(observation);
        }
        await Assert.That(captured.Select(node => node.Status.NodeId).Distinct(StringComparer.Ordinal).Count())
            .IsEqualTo(QueueLeaseClusterProtocol.Nodes.Count);
        var selected = captured[^1];
        var remaining = captured.Where(node => node.ResourceName != selected.ResourceName)
            .Select(node => node.ResourceName).ToArray();
        return new(selected, remaining);
    }

    /// <summary>Requires every survivor to report exactly the surviving silos once Orleans membership removed the killed one.</summary>
    internal async Task RequireAvailableSilosAsync(ClusterFixture fixture, CancellationToken token)
    {
        await ClusterMembershipReadiness.WaitForActiveSilosAsync(fixture, survivors, token);
        foreach (var node in survivors)
        {
            var current = await ReadAsync(fixture, node, token);
            await Assert.That(current.Status.SiloCount).IsEqualTo(survivors.Length);
            await Assert.That(current.ActiveSilos.ToHashSet(StringComparer.Ordinal).SetEquals(survivors)).IsTrue();
        }
    }

    internal async Task RequireRejoinedAsync(ClusterFixture fixture, CancellationToken token)
    {
        await ClusterMembershipReadiness.WaitForActiveSilosAsync(fixture, QueueLeaseClusterProtocol.Nodes, token);
        var restored = await ReadAsync(fixture, OriginalSiloNode, token);
        await Assert.That(restored.Status.SiloCount).IsEqualTo(QueueLeaseClusterProtocol.Nodes.Count);
        await Assert.That(restored.SiloName).IsEqualTo(original.SiloName);
        await Assert.That(restored.Status.NodeId).IsEqualTo(original.Status.NodeId);
        await Assert.That(restored.Status.Incarnation).IsEqualTo(original.Status.Incarnation);
        await Assert.That(restored.ProcessInstance).IsNotEqualTo(original.ProcessInstance);
        await Assert.That(restored.ActiveSilos.ToHashSet(StringComparer.Ordinal)
            .SetEquals(QueueLeaseClusterProtocol.Nodes)).IsTrue();
    }

    private static Task<QueueDeadlineSiloClusterObservation> ReadAsync(ClusterFixture fixture,
        string node, CancellationToken token)
        => QueueDeadlineClusterCallers.UseAsync(fixture, node, fixture.AdminKey, async admin =>
        {
            var status = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.StatusAsync(token));
            var snapshot = (await McpCallerAssertions.SuccessAsync<AdminNodeSnapshot>(
                await admin.Mcp.CallWithoutBodyAsync(AdminDashboardProtocol.SnapshotTool, token))).Value;
            await Assert.That(status.RoutingReady).IsTrue();
            await Assert.That(status.ReplicaCount).IsEqualTo(QueueLeaseClusterProtocol.Nodes.Count);
            await Assert.That(snapshot.Node.NodeId).IsEqualTo(status.NodeId);
            await Assert.That(snapshot.Node.Incarnation).IsEqualTo(status.Incarnation);
            await Assert.That(snapshot.SiloName).IsEqualTo(node);
            await Assert.That(snapshot.ActiveSilos.Contains(node, StringComparer.Ordinal)).IsTrue();
            return new QueueDeadlineSiloClusterObservation(node, status, snapshot.SiloName!,
                snapshot.ActiveSilos.ToArray(), snapshot.Http.ProcessInstance);
        }, token);
}
