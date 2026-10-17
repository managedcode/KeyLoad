namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal enum NativeStoreOpenPreflightDefect
{
    Checksum, Sequence, Semantics, UnsupportedTornPayload, Checkpoint, PayloadLengthMismatch
}
