namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal enum BlobRetiredPartFaultKind
{
    MissingRaw, MissingManifest, WrongManifestHash
}
