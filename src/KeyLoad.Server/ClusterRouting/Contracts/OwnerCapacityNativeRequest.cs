
namespace KeyLoad.Server.Features.ClusterRouting;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(OwnerCapacityNativeRequestAliases.OwnerCapacityNativeRequestAlias)]
internal sealed record OwnerCapacityNativeRequest(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] Guid RequestId,
    [property: global::Orleans.Id(2)] Guid Nonce,
    [property: global::Orleans.Id(3)] string PrincipalId,
    [property: global::Orleans.Id(4)] long PolicyEpoch,
    [property: global::Orleans.Id(5)] RegisteredPhysicalOwnerV1 Control,
    [property: global::Orleans.Id(6)] RegisteredPhysicalOwnerV1 Destination,
    [property: global::Orleans.Id(7)] string CallerVoter,
    [property: global::Orleans.Id(8)] string CallerSiloAddress,
    [property: global::Orleans.Id(9)] string DestinationVoter,
    [property: global::Orleans.Id(10)] string DestinationSiloAddress,
    [property: global::Orleans.Id(11)] string CohortFingerprint,
    [property: global::Orleans.Id(12)] DateTimeOffset IssuedAt,
    [property: global::Orleans.Id(13)] DateTimeOffset ExpiresAt,
    [property: global::Orleans.Id(14)] System.Collections.Immutable.ImmutableArray<PartitionRef> Partitions,
    [property: global::Orleans.Id(15)] string Signature,
    [property: global::Orleans.Id(16)] long MaximumReadBytes,
    [property: global::Orleans.Id(17)] int MaximumExaminedRecords,
    [property: global::Orleans.Id(18)] int MaximumResultBytes);
