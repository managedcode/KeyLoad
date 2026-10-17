using KeyLoad.Replication;
using KeyLoad.Storage;

namespace KeyLoad.RecoveryTests.Features.ClusterReplication;

internal sealed record ReplicaSnapshotInstallFencingCut(string[] Canonical, long CanonicalPosition,
    string[] Replica, long ReplicaPosition, byte[] HardState, StoreIdentity CanonicalIdentity,
    StoreIdentity ReplicaIdentity, ReplicaSnapshot? Published, ReplicaSnapshotInstallFencingFile[] Files);

internal sealed record ReplicaSnapshotInstallFencingFile(string Name, long Length, string Digest, byte[]? PrivateBytes);
