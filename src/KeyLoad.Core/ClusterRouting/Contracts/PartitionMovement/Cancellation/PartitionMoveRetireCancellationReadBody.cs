namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

/// <summary>Fresh authenticated observation scope; it never authorizes another cancellation effect.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveParentContractNames.PartitionMoveRetireCancellationReadAlias)]
internal sealed record PartitionMoveRetireCancellationReadBody(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid OriginalPhaseCommandId,
    [property: Orleans.Id(2)] Guid CancellationCommandId,
    [property: Orleans.Id(3)] PartitionMovePeerEnvelope OriginalEnvelope,
    [property: Orleans.Id(4)] PartitionMoveJournalReceipt OriginalAuthorization,
    [property: Orleans.Id(5)] ReadOnlyMemory<byte> OriginalRequestBytes,
    [property: Orleans.Id(6)] string OriginalRequestSignature,
    [property: Orleans.Id(7)] DateTimeOffset QueryExpiresAt);
