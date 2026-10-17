namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

/// <summary>Actual source-committed cancellation of one immutable expired first-issued Retire.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveParentContractNames.PartitionMoveExpiredRetireCancellationAlias)]
internal sealed record PartitionMoveExpiredRetireCancellation(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid OriginalPhaseCommandId,
    [property: Orleans.Id(2)] string OriginalPhaseIdentityDigest,
    [property: Orleans.Id(3)] Guid OriginalRequestNonce,
    [property: Orleans.Id(4)] DateTimeOffset OriginalExpiresAt,
    [property: Orleans.Id(5)] Guid CancellationCommandId,
    [property: Orleans.Id(6)] PartitionMoveJournalReceipt CancellationReceipt,
    [property: Orleans.Id(7)] string CancellationPrincipalId,
    [property: Orleans.Id(8)] long CancellationPolicyEpoch,
    [property: Orleans.Id(9)] long CleanupGeneration,
    [property: Orleans.Id(10)] int FamilyOrdinal,
    [property: Orleans.Id(11)] int BatchOrdinal,
    [property: Orleans.Id(12)] PartitionMovePhaseCommand CancellationPhase);
