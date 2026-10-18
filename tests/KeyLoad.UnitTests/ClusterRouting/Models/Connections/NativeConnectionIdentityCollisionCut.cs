namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed record NativeConnectionIdentityCollisionCut(string[] RowHashes, long Position,
    (long AppliedPosition, long StorePosition) Cut);
