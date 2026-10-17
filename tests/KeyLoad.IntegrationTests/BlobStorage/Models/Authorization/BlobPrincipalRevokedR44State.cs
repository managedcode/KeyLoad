using KeyLoad.IntegrationTests.Features.ClientApi;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal sealed class BlobPrincipalRevokedR44State(BlobBoundedRetirementPublicPublication.Published published,
    AtomicPartitionPlacementResolution placement, McpPersistedIdentity identity, OutboxStatus outbox)
{
    internal BlobBoundedRetirementPublicPublication.Published Published { get; } = published;
    internal AtomicPartitionPlacementResolution Placement { get; } = placement;
    internal McpPersistedIdentity Identity { get; } = identity;
    internal OutboxStatus Outbox { get; set; } = outbox;
}
