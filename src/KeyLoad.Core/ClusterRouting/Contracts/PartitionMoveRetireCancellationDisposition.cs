namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveParentContractNames.PartitionMoveRetireCancellationDispositionAlias)]
internal sealed record PartitionMoveRetireCancellationDisposition(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid OriginalPhaseCommandId,
    [property: Orleans.Id(2)] string OriginalPhaseIdentityDigest,
    [property: Orleans.Id(3)] Guid OriginalNonce,
    [property: Orleans.Id(4)] DateTimeOffset OriginalExpiresAt,
    [property: Orleans.Id(5)] Guid CancellationCommandId,
    [property: Orleans.Id(6)] PartitionMoveJournalReceipt CancellationReceipt,
    [property: Orleans.Id(7)] string ReceiverPrincipalId,
    [property: Orleans.Id(8)] long ReceiverPolicyEpoch,
    [property: Orleans.Id(9)] long CleanupGeneration);
