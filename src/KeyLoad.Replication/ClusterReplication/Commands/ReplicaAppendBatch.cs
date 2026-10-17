namespace KeyLoad.Replication;

internal sealed record ReplicaAppendBatch(ReplicaEntry[] Entries, byte[][] Encoded);
