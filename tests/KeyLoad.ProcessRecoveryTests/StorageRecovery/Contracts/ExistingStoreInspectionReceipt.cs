namespace KeyLoad.ProcessRecoveryTests;

internal sealed record ExistingStoreInspectionReceipt(int SchemaVersion, bool Success, Guid NodeId,
    Guid Incarnation, long Position, byte[]? Value, string[] FailureTypes,
    string? ErrorCode, int ObservedStages)
{
    public override string ToString() => nameof(ExistingStoreInspectionReceipt);
}
