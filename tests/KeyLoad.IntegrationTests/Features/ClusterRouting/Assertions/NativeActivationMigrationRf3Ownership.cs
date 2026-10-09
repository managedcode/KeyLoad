using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeActivationMigrationRf3Ownership
{
    internal static async Task<AtomicPartitionPlacementResolution> ReadPlacementAsync(RequestCqrsRf3Callers administrator,
        PartitionRef partition, CancellationToken cancellationToken)
        => await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ReadAtomicPartitionPlacementAsync(
            new(NativeActivationMigrationRf3Protocol.PlacementVersion, partition), cancellationToken).ConfigureAwait(false));

    internal static async Task RequirePlacementAsync(RequestCqrsRf3Callers administrator, PartitionRef partition,
        AtomicPartitionPlacementResolution original, CancellationToken cancellationToken)
    {
        var current = await ReadPlacementAsync(administrator, partition, cancellationToken).ConfigureAwait(false);
        await Assert.That(JsonDefaults.Serialize(current).SequenceEqual(JsonDefaults.Serialize(original))).IsTrue();
    }

    internal static async Task<NodeStatus[]> CaptureAsync(TwoRf3MembershipWave wave, CancellationToken cancellationToken)
    {
        var observations = new List<NodeStatus>();
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            using var http = McpCallerHttp.Create(wave.Application, node);
            var client = new KeyLoadClient(http, wave.Profile.AdminKey, IntegrationClientOptions.Execution());
            var status = await McpCallerAssertions.SdkSuccessAsync(await client.StatusAsync(cancellationToken).ConfigureAwait(false));
            await Assert.That(status.RoutingReady).IsTrue();
            await Assert.That(status.Voters).IsEqualTo(TwoRf3MembershipProtocol.MembersPerGroup);
            observations.Add(status);
        }
        await Assert.That(observations.Select(value => value.NodeId).Distinct().Count()).IsEqualTo(TwoRf3MembershipProtocol.Nodes.Length);
        return [.. observations];
    }

    internal static async Task RequirePhysicalAsync(NodeStatus[] original, NodeStatus[] current)
    {
        await Assert.That(current.Length).IsEqualTo(original.Length);
        for (var index = NativeActivationMigrationRf3Protocol.FirstNode; index < original.Length; index++)
        {
            await Assert.That(current[index].NodeId).IsEqualTo(original[index].NodeId);
            await Assert.That(current[index].Incarnation).IsEqualTo(original[index].Incarnation);
            await Assert.That(current[index].Voters).IsEqualTo(original[index].Voters);
        }
    }
}
