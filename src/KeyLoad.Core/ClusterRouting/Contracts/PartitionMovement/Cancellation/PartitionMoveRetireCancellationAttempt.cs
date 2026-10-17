namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

/// <summary>Immutable first signed transport attempt, retained before send without inventing receiver issuance.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveParentContractNames.PartitionMoveRetireCancellationAttemptAlias)]
internal sealed record PartitionMoveRetireCancellationAttempt(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid CancellationCommandId,
    [property: Orleans.Id(2)] Guid CancellationNonce,
    [property: Orleans.Id(3)] DateTimeOffset CancellationExpiresAt,
    [property: Orleans.Id(4)] ReadOnlyMemory<byte> OriginalRequestBytes,
    [property: Orleans.Id(5)] string OriginalRequestSignature);
