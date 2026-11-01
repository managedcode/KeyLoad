namespace KeyLoad.ProcessRecoveryTests;

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
    Observer,
    WaitBeforeOpen
}
