namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal sealed record BlobPriorPublicationFault(ReplicatedOperation Delete, ReplicatedOperation Reclaim,
    ReplicatedOperation QueueOperation, string[] OriginalImage, string[] DamagedImage, long Position,
    byte[] Key, byte[] OriginalRow);
