namespace KeyLoad.RecoveryTests.Features.ClusterReplication;

internal sealed record ReplicaSnapshotCancellationRecoveryFile(string Name, long Length, string Digest, byte[] Bytes);
