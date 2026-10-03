namespace KeyLoad.CrashHost;

internal enum ExistingStoreInspectionVariant
{
    Normal,
    NullOptions,
    EmptyNodeId,
    MissingIncarnation,
    EmptyIncarnation,
    RelativeDirectory,
    EmptyDirectory,
    NonCanonicalDirectory,
    ZeroFrameBudget,
    ZeroSnapshotBudget,
    Cache,
    Observer,
    WaitBeforeOpen
}

internal sealed record ExistingStoreInspectionRequest(string Directory, Guid ExpectedNodeId,
    Guid? Incarnation, ExistingStoreInspectionVariant Variant)
{
    public override string ToString() => nameof(ExistingStoreInspectionRequest);
}

internal sealed record ExistingStoreInspectionReceipt(int SchemaVersion, bool Success, Guid NodeId,
    Guid Incarnation, int FormatVersion, long Position, byte[]? Value, string[] FailureTypes,
    string? ErrorCode, int ObservedStages, long RetainedBytes)
{
    public override string ToString() => nameof(ExistingStoreInspectionReceipt);
}
