namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed record NativeConnectionIdentityCollisionCut(string[] RowHashes, long Position,
    (long LastIndex, long CommittedIndex, long Term) Journal);
