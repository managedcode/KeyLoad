using KeyLoad.Replication;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed record ReplicaRetainedReplayRestartCut(string Directory, string CanonicalDirectory,
    string ConfigurationJson, StoreIdentity CanonicalIdentity, StoreIdentity ReplicaIdentity,
    ReplicaHardState State, long Applied, long Position, string[] Rows, byte[][] Entries,
    byte[] CanonicalSigningDigest, byte[] ReplicaSigningDigest);
