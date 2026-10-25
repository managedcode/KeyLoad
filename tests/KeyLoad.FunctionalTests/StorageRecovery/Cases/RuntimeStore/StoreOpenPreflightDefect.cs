namespace KeyLoad.FunctionalTests.StorageRecovery;

internal enum StoreOpenPreflightDefect
{
    Checksum, Sequence, Semantics, UnsupportedTornPayload, Checkpoint, PayloadLengthMismatch
}
