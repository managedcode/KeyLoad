using System.Collections.Immutable;
using KeyLoad.Replication;

namespace KeyLoad.RecoveryTests;

internal sealed record ReplicaCheckpointProtocolPlanningObservation(ReplicaHardState State, long Term,
    ImmutableArray<ReplicaEntry> Suffix);
