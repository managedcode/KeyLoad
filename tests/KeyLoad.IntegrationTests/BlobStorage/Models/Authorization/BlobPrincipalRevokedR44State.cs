using KeyLoad.IntegrationTests.Features.ClientApi;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal sealed class BlobPrincipalRevokedR44State(BlobBoundedRetirementPublicPublication.Published published,
    McpPersistedIdentity identity, OutboxStatus outbox)
{
    internal BlobBoundedRetirementPublicPublication.Published Published { get; } = published;
    internal McpPersistedIdentity Identity { get; } = identity;
    internal OutboxStatus Outbox { get; set; } = outbox;
}
