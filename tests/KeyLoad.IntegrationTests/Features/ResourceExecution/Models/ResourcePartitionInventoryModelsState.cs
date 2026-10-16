using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.DatabaseComposition;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal sealed record ResourcePartitionInventoryModelsStep(ResourceDefinition Definition,
    ImmutableArray<Mutation> Mutations, ImmutableArray<MutationReceipt> Expected);

internal sealed record ResourcePartitionInventoryModelsBatch(CommandRequest Command, CommitReceipt Receipt);

internal sealed record ResourcePartitionInventoryModelsInventory(ResourceDefinition Definition,
    ResourceOwnerInventoryResultV1 Result);

internal sealed record ResourcePartitionInventoryModelsState(
    ImmutableArray<ResourcePartitionInventoryRf3Creation> Creations,
    ImmutableArray<ResourcePartitionInventoryModelsBatch> Batches,
    FunctionalGateColdModel Model,
    ImmutableArray<ResourcePartitionInventoryModelsInventory> Inventory);

internal sealed record ResourcePartitionInventoryModelsTableOriginal(SqlOperationRequest Create,
    ResourceDefinition Definition, SqlOperationRequest Insert, CommitReceipt Receipt);
