namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Fresh query authority enclosing the unchanged original movement phase identity.</summary>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(PartitionMovementProtocol.OutcomeRequestAlias)]
internal sealed record PartitionMovementOutcomeTransportRequest(
    [property: global::Orleans.Id(0)] PartitionMovementTransportRequest Original,
    [property: global::Orleans.Id(1)] DateTimeOffset ExpiresAt,
    [property: global::Orleans.Id(2)] Guid Nonce,
    [property: global::Orleans.Id(3)] long MaximumReadBytes,
    [property: global::Orleans.Id(4)] int MaximumExaminedRecords,
    [property: global::Orleans.Id(5)] int MaximumResultBytes);
