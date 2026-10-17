using KeyLoad.Replication;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal sealed record PartitionWriterOrleansOriginal(string Node, ReplicaEntry Entry, OperationResult Result,
    byte[] HardState);
