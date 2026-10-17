namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

/// <summary>Original signed cancellation request; current receiver identity is assigned by native admission.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveParentContractNames.PartitionMoveRetireCancellationBodyAlias)]
internal sealed record PartitionMoveRetireCancellationBody(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid CancellationCommandId,
    [property: Orleans.Id(2)] Guid OriginalPhaseCommandId,
    [property: Orleans.Id(3)] PartitionMovePeerEnvelope OriginalEnvelope,
    [property: Orleans.Id(4)] PartitionMoveJournalReceipt OriginalAuthorization,
    [property: Orleans.Id(5)] long CleanupGeneration,
    [property: Orleans.Id(6)] ReadOnlyMemory<byte> OriginalRequestBytes,
    [property: Orleans.Id(7)] string OriginalRequestSignature,
    [property: Orleans.Id(8)] DateTimeOffset CancellationExpiresAt,
    [property: Orleans.Id(9)] string? ActualReceiverPrincipalId = null,
    [property: Orleans.Id(10)] long ActualReceiverPolicyEpoch = PartitionMoveParentContractNames.UnissuedPolicyEpoch,
    [property: Orleans.Id(11)] PartitionMoveReceiverSourceWitness? OriginalSourceWitness = null);
