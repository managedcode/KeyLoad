using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal sealed record DropTableLifecycleSeedState(PartitionRef Partition,
    ImmutableArray<DropTableLifecycleTable> Tables, ImmutableArray<ResourceDefinition> Controls,
    CommandRequest Original, CommitReceipt Receipt, DocumentResult Anchor, DocumentResult ControlRow);

internal sealed record DropTableLifecycleTable(string Name, ResourceDefinition Definition,
    Guid CreateCommandId, ConfigureResourceRequest CreateRequest, string CreateSql,
    ImmutableArray<DocumentResult> Rows, ResourceOwnerInventoryResultV1 Inventory)
{
    internal ResourceLifetimeV1 Lifetime => Inventory.Control.Lifetime;
    internal DropTableRequest Request => new(Rows[DropTableLifecycleSeedProtocol.First].Reference.Partition.TenantId,
        Rows[DropTableLifecycleSeedProtocol.First].Reference.Partition.DatabaseId, Name);
}

internal sealed record DropTableLifecycleCreation(Guid CommandId, ResourceDefinition Definition);

internal sealed record DropTableLifecycleBoundSeed(ImmutableArray<ResourceOwnerInventoryResultV1> Before,
    ImmutableArray<ImmutableArray<DocumentResult>> Rows, DocumentResult Anchor, DocumentResult Control);
