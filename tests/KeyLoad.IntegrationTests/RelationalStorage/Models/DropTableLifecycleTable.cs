using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal sealed record DropTableLifecycleTable(string Name, ResourceDefinition Definition,
    Guid CreateCommandId, ConfigureResourceRequest CreateRequest, string CreateSql,
    ImmutableArray<DocumentResult> Rows, ResourceOwnerInventoryResultV1 Inventory)
{
    internal ResourceLifetimeV1 Lifetime => Inventory.Control.Lifetime;
    internal DropTableRequest Request => new(Rows[DropTableLifecycleSeedProtocol.First].Reference.Partition.TenantId,
        Rows[DropTableLifecycleSeedProtocol.First].Reference.Partition.DatabaseId, Name);
}
