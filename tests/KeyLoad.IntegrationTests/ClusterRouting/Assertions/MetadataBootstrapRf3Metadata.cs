using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class MetadataBootstrapRf3Metadata
{
    internal static async Task CaptureAsync(MetadataBootstrapRf3State state, CancellationToken token)
    {
        var profile = ClusterFixturePhysicalShardIdentity.ReadProfile(state.Fixture.Root);
        state.Owner = new(1, state.First, state.Fixture.PhysicalShardId, profile.Incarnation,
            [RequestCqrsProbeFixtureProtocol.Node1Origin, RequestCqrsProbeFixtureProtocol.Node2Origin, RequestCqrsProbeFixtureProtocol.Node3Origin],
            1L, 0L, 0L, true);
        await RequirePartitionAsync(state, state.First, 0L, false, state.Fixture.AdminKey, token);
    }
    internal static async Task RequirePartitionAsync(MetadataBootstrapRf3State state, PartitionRef partition,
        long directory, bool assigned, string credential, CancellationToken token)
    {
        var expected = state.Owner with
        {
            Partition = partition,
            DirectoryRevision = directory,
            Revision = assigned ? 1L : 0L,
            IsFallback = !assigned
        };
        var request = new AtomicPartitionPlacementReadRequest(1, partition);
        var sdk = await MetadataBootstrapRf3Calls.SdkAsync(state, credential,
            client => client.ReadAtomicPartitionPlacementAsync(request, token));
        await AtomicPartitionPlacementRf3Assertions.SameWitnessAsync(expected, sdk);
        await MetadataBootstrapRf3Calls.McpAsync(state, credential, async mcp =>
        {
            await AtomicPartitionPlacementRf3Assertions.SameWitnessAsync(expected,
                await AtomicPartitionPlacementRf3Assertions.McpReadAsync(mcp, request, token));
            return true;
        }, token);
    }
    internal static async Task AllAsync(MetadataBootstrapRf3State state, CancellationToken token)
    {
        var directory = (long)state.Binds.Count;
        foreach (var partition in new[] { state.First, state.Second, state.Third })
        {
            await RequirePartitionAsync(state, partition, directory,
                state.Binds.Any(bind => bind.Request.Partition == partition), state.Fixture.AdminKey, token);
        }
    }
    internal static async Task BindNextAsync(MetadataBootstrapRf3State state, PartitionRef partition,
        string credential, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var request = new BindAtomicPartitionPlacementRequest(1, state.Binds.Count, partition, state.Fixture.PhysicalShardId);
        await MetadataBootstrapRf3Calls.BindAsync(state, credential, id, request, token);
        state.Binds.Add((id, request, credential));
        await AllAsync(state, token);
    }
}
