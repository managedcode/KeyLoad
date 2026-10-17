using KeyLoad.Replication;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal sealed record EmptyReplicaSnapshotCut(ReplicaHardState State, ReplicaEntry Tail);
