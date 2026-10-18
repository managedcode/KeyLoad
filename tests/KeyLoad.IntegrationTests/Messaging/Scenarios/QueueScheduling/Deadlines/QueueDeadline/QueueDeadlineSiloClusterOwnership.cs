using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

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
            captured.Add(await ReadAsync(fixture, name, token));
        }
        await Assert.That(captured.Select(node => node.Status.NodeId).Distinct(StringComparer.Ordinal).Count())
            .IsEqualTo(QueueLeaseClusterProtocol.Nodes.Count);
        var selected = captured[^1];
        var remaining = captured.Where(node => node.ResourceName != selected.ResourceName)
            .Select(node => node.ResourceName).ToArray();
        return new(selected, remaining);
    }

    internal async Task RequireAvailableSilosAsync(ClusterFixture fixture, CancellationToken token)
    {
        var until = TimeProvider.System.GetUtcNow() + DueRecurringClusterProtocol.ProgressWindow;
        while (TimeProvider.System.GetUtcNow() < until)
        {
            var current = new List<QueueDeadlineSiloClusterObservation>();
            foreach (var node in survivors)
            {
                current.Add(await ReadAsync(fixture, node, token));
            }
            if (current.All(node => node.Status.RoutingReady && node.ActiveSilos.ToHashSet(StringComparer.Ordinal)
                .SetEquals(survivors)))
            {
                return;
            }
            await Task.Delay(DueRecurringClusterProtocol.PollInterval, TimeProvider.System, token);
        }
        throw new TimeoutException("Native Orleans membership did not observe the available silos before the deadline.");
    }

    internal async Task RequireRejoinedAsync(ClusterFixture fixture, CancellationToken token)
    {
        var restored = await ReadAsync(fixture, OriginalSiloNode, token);
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
            await Assert.That(status.SiloCount).IsEqualTo(QueueLeaseClusterProtocol.Nodes.Count);
            await Assert.That(status.ReplicaCount).IsEqualTo(QueueLeaseClusterProtocol.Nodes.Count);
            await Assert.That(snapshot.Node.NodeId).IsEqualTo(status.NodeId);
            await Assert.That(snapshot.Node.Incarnation).IsEqualTo(status.Incarnation);
            await Assert.That(snapshot.SiloName).IsEqualTo(node);
            await Assert.That(snapshot.ActiveSilos.Contains(node, StringComparer.Ordinal)).IsTrue();
            return new QueueDeadlineSiloClusterObservation(node, status, snapshot.SiloName!,
                snapshot.ActiveSilos.ToArray(), snapshot.Http.ProcessInstance);
        }, token);
}
