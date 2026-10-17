using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal sealed record ResourcePartitionInventoryRf3Seed(string Credential,
    ImmutableArray<ResourcePartitionInventoryRf3Creation> Creations,
    ImmutableArray<ResourcePartitionInventoryRf3Original> Originals,
    CommandRequest LateFailure)
{
    internal static ImmutableArray<PartitionRef> Partitions =>
    [
        ResourcePartitionInventoryRf3Protocol.Partition(ResourcePartitionInventoryRf3Protocol.FirstPartitionKey),
        ResourcePartitionInventoryRf3Protocol.Partition(ResourcePartitionInventoryRf3Protocol.SecondPartitionKey),
        ResourcePartitionInventoryRf3Protocol.Partition(ResourcePartitionInventoryRf3Protocol.ThirdPartitionKey)
    ];
}
