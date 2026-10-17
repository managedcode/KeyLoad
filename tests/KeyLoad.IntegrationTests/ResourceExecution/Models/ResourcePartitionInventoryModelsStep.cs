using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal sealed record ResourcePartitionInventoryModelsStep(ResourceDefinition Definition,
    ImmutableArray<Mutation> Mutations, ImmutableArray<MutationReceipt> Expected);
