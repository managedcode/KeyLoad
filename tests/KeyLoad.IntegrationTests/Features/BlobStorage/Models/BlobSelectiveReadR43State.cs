using KeyLoad.IntegrationTests.Features.ClientApi;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal sealed class BlobSelectiveReadR43State(BlobSupersessionPublicTrial.Version[] versions,
    AtomicPartitionPlacementResolution placement, McpPersistedIdentity identity)
{
    internal BlobSupersessionPublicTrial.Version[] Versions { get; } = versions;
    internal AtomicPartitionPlacementResolution Placement { get; } = placement;
    internal McpPersistedIdentity Identity { get; set; } = identity;
    internal PrincipalRecord Both { get; set; } = identity.Principal;
    internal OutboxStatus Outbox { get; set; } = null!;
    internal static byte[] Bytes(int index) => index == 0 ? [11, 23, 37, 53] : [13, 27, 41, 55, 69, 83, 97, 111];
}
