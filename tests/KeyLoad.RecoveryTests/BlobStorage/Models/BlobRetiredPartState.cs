namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal sealed record BlobRetiredPartState(BlobPriorPublicationState Publication,
    BlobPriorPublicationCommand Deletion, BlobMetadata Tombstone);
