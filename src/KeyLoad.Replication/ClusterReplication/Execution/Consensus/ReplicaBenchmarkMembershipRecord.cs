using System.Collections.Immutable;

namespace KeyLoad.Replication;

[Orleans.GenerateSerializer]
[Orleans.Alias(ReplicaSerializationAliases.BenchmarkMembership)]
internal sealed record ReplicaBenchmarkMembershipRecord([property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid Incarnation, [property: Orleans.Id(2)] ImmutableArray<string> VoterIds);
