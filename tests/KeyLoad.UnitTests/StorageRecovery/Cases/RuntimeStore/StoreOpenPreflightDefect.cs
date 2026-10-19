namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal enum StoreOpenPreflightDefect
{
    Checksum, Sequence, Semantics, UnsupportedTornPayload, Checkpoint, PayloadLengthMismatch
}
