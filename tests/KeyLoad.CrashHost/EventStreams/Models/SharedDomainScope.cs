using System.Collections.Immutable;

namespace KeyLoad.CrashHost.Features.EventStreams;

internal sealed record SharedDomainScope(PartitionRef Partition, string? Documents, string? Streams, string? Queue, string Payload)
{
    internal ImmutableArray<ResourceDefinition> Resources
    {
        get
        {
            var resources = ImmutableArray.CreateBuilder<ResourceDefinition>();
            if (Documents is not null)
            {
                resources.Add(new(Documents, ResourceKind.Collection, Partition.TransactionDomainId));
            }
            if (Streams is not null)
            {
                resources.Add(new(Streams, ResourceKind.StreamSet, Partition.TransactionDomainId));
            }
            if (Queue is not null)
            {
                resources.Add(new(Queue, ResourceKind.WorkQueue, Partition.TransactionDomainId));
            }
            return resources.ToImmutable();
        }
    }
}
