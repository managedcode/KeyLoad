using System.Collections.Immutable;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.QueryExecution;

internal sealed record SparseCompositePrefixClusterState(PartitionRef Partition, bool IncludeNull, bool IncludeMissing,
    CommandRequest Seed, CommitReceipt Receipt, McpPersistedIdentity Identity, OutboxStatus Outbox,
    ImmutableDictionary<string, EntityRefLifetime> Lifetimes)
{
    internal EntityRef Stored(string collection, string id) => new(Partition, collection, id) { Lifetime = Lifetimes[collection] };
}
