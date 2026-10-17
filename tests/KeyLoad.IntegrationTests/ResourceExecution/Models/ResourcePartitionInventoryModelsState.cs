using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.DatabaseComposition;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal sealed record ResourcePartitionInventoryModelsState(
    ImmutableArray<ResourcePartitionInventoryRf3Creation> Creations,
    ImmutableArray<ResourcePartitionInventoryModelsBatch> Batches,
    FunctionalGateColdModel Model,
    ImmutableArray<ResourcePartitionInventoryModelsInventory> Inventory);
