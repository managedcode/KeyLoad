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
