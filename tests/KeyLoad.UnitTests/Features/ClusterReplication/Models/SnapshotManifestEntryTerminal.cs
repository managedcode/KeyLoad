using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed record SnapshotManifestEntryTerminal(ReplicatedOperation Healthy, string[] Canonical,
    long CanonicalPosition, string[] Replica, long ReplicaPosition, byte[] HardState, StoreIdentity Identity);
