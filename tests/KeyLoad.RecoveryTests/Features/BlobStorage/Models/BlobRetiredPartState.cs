namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal enum BlobRetiredPartFaultKind { MissingRaw, MissingManifest, WrongManifestHash }

internal sealed record BlobRetiredPartState(BlobPriorPublicationState Publication,
    BlobPriorPublicationCommand Deletion, BlobMetadata Tombstone);

internal sealed record BlobRetiredPartOwnedRow(byte[] Key, byte[] Original);
