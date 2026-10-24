namespace KeyLoad.UnitTests.StorageRecovery;

internal enum StoreOpenPreflightDefect
{
    Checksum, Sequence, Semantics, UnsupportedTornPayload, Checkpoint, PayloadLengthMismatch
}
