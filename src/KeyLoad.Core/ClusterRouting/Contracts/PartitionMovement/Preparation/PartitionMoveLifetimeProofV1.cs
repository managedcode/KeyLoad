namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

/// <summary>Binds the original issued grant and authorizing operation to the original command proof.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(PartitionResourceLifetimeContract.MoveProofAlias)]
internal sealed record PartitionMoveLifetimeProofV1(
    [property: Orleans.Id(PartitionResourceLifetimeContract.MoveGrantId)] PartitionMovePhaseGrant OriginalGrant,
    [property: Orleans.Id(PartitionResourceLifetimeContract.MoveAuthorizationFingerprintId)] string AuthorizationFingerprint,
    [property: Orleans.Id(PartitionResourceLifetimeContract.MoveControlProofId)] PartitionControlLifetimeProofV1 ControlProof);
