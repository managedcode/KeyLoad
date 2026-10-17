using KeyLoad.Replication;
using KeyLoad.Storage;

namespace KeyLoad.RecoveryTests.Features.ClusterReplication;

internal sealed record ReplicaSnapshotCancellationRecoveryCut(string[] Canonical, long CanonicalPosition,
    string[] Replica, long ReplicaPosition, byte[] HardState, StoreIdentity CanonicalIdentity,
    StoreIdentity ReplicaIdentity, ReplicaSnapshot? Published, ReplicaSnapshotCancellationRecoveryFile[] Files);
