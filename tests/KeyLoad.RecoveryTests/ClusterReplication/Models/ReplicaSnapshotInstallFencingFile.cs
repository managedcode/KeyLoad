namespace KeyLoad.RecoveryTests.Features.ClusterReplication;

internal sealed record ReplicaSnapshotInstallFencingFile(string Name, long Length, string Digest, byte[]? PrivateBytes);
