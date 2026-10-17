namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed record ReplicaRetainedReplayCut(long Position, string[] Rows, long LastIndex,
    long CommittedIndex, long AppliedIndex, long Term, byte[] Entry);
