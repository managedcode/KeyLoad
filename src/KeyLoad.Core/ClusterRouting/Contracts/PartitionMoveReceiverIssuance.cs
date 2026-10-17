namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

/// <summary>Receiver-owned first identity and policy, with no self-referential future receipt.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveParentContractNames.PartitionMoveReceiverIssuanceAlias)]
internal sealed record PartitionMoveReceiverIssuance(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid MoveId,
    [property: Orleans.Id(2)] PartitionRef Partition,
    [property: Orleans.Id(3)] Guid OriginalPhaseCommandId,
    [property: Orleans.Id(4)] string OriginalPhaseIdentityDigest,
    [property: Orleans.Id(5)] Guid OriginalRequestNonce,
    [property: Orleans.Id(6)] PartitionMovePeerStage OriginalStage,
    [property: Orleans.Id(7)] int OriginalPageOrdinal,
    [property: Orleans.Id(8)] Guid OriginalGrantId,
    [property: Orleans.Id(9)] string SourceOperatorPrincipalId,
    [property: Orleans.Id(10)] long SourceOperatorPolicyEpoch,
    [property: Orleans.Id(11)] string ReceiverPrincipalId,
    [property: Orleans.Id(12)] long ReceiverIssuancePolicyEpoch,
    [property: Orleans.Id(13)] DateTimeOffset OriginalExpiresAt,
    [property: Orleans.Id(14)] PhysicalShardRecord ReceiverOwner,
    [property: Orleans.Id(15)] long IssuanceAppliedPosition,
    [property: Orleans.Id(16)] PartitionMoveJournalReceipt OriginalAuthorization,
    [property: Orleans.Id(17)] string OriginalBodyDigest,
    [property: Orleans.Id(18)] string OriginalControlIntentDigest,
    [property: Orleans.Id(19)] Guid IssuanceCommandId,
    [property: Orleans.Id(20)] string IssuanceFingerprint);
