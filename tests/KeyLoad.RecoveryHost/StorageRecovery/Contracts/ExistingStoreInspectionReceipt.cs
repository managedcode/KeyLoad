namespace KeyLoad.RecoveryHost;

internal sealed record ExistingStoreInspectionReceipt(int SchemaVersion, bool Success, Guid NodeId,
    Guid Incarnation, int FormatVersion, long Position, byte[]? Value, string[] FailureTypes,
    string? ErrorCode, int ObservedStages, long RetainedBytes)
{
    public override string ToString() => nameof(ExistingStoreInspectionReceipt);
}
