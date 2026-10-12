using KeyLoad.Replication;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal sealed record PartitionWriterOrleansImage(string Node, string[] Canonical, long CanonicalPosition,
    byte[] CanonicalIdentity, string[] Replica, long ReplicaPosition, byte[] ReplicaIdentity);

internal sealed record PartitionWriterOrleansOriginal(string Node, ReplicaEntry Entry, OperationResult Result,
    byte[] HardState);
