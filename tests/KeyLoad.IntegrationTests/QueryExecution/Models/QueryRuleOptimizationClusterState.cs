using System.Collections.Immutable;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal sealed record QueryRuleOptimizationClusterState(PartitionRef Partition, bool IncludeNull, bool IncludeMissing,
    McpPersistedIdentity Reader, OutboxStatus Outbox,
    (CommandRequest Command, CommitReceipt Receipt)[] Originals, ImmutableDictionary<string, EntityRefLifetime> Lifetimes)
{
    internal EntityRef Stored(string collection, string id) => new(Partition, collection, id) { Lifetime = Lifetimes[collection] };
}
