namespace KeyLoad.Server.Features.BlobStorage;

internal sealed record NativeBlobWireArms(int Version, string SessionId, string OwnerDigest,
    Guid MoveId, string PrincipalId, string TenantId, long PolicyEpoch, PartitionRef Partition,
    string ResourceName, ResourceKind ResourceKind, string TransactionDomainId, NativeBlobWireArm[] Arms);
