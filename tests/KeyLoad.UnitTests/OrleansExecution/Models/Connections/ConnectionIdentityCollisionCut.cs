namespace KeyLoad.UnitTests.OrleansExecution;

internal sealed record ConnectionIdentityCollisionCut(string[] RowHashes, long Position,
    (long AppliedPosition, long StorePosition) Cut);
