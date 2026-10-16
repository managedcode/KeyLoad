using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal sealed record DropTableLifecycleFreshState(ResourceDefinition Definition, CommandRequest Write,
    CommitReceipt Receipt, ImmutableArray<DocumentResult> Rows, ResourceOwnerInventoryResultV1 Inventory);
