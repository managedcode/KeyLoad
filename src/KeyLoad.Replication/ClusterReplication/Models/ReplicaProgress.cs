namespace KeyLoad.Replication;

internal sealed record ReplicaProgress(long NextIndex, long MatchedIndex);
