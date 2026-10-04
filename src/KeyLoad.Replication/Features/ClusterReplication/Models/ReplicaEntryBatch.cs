using System.Collections.Immutable;

namespace KeyLoad.Replication;

/// <summary>Defines the native standalone entry batch used by the configured append byte budget.</summary>
/// <param name="Entries">Initialized ordered entries, including an empty heartbeat batch.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ReplicaSerializationAliases.ReplicaEntryBatch)]
public sealed record ReplicaEntryBatch([property: Orleans.Id(0)] ImmutableArray<ReplicaEntry> Entries);
