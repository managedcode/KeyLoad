using KeyLoad.Replication;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed record SnapshotManifestEntryState(ReplicaSnapshot Snapshot, string ManifestPath, string PrefixPath,
    byte[] Manifest, byte[] Prefix, string SourceImagePath, byte[] SourceImage, string[] Canonical, long CanonicalPosition, string[] Replica,
    long ReplicaPosition, byte[] HardState, StoreIdentity Identity);
