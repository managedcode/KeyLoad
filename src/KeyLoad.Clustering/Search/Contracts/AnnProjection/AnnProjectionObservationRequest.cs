using System.Collections.Immutable;
using KeyLoad.Core.StoragePartitions.Contracts;

namespace KeyLoad.Clustering;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(AnnProjectionObservationProtocol.RequestAlias)]
internal sealed record AnnProjectionObservationRequest(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] AnnProjectionObservationKind Kind,
    [property: global::Orleans.Id(2)] AnnMaintenanceRequest OriginalRequest,
    [property: global::Orleans.Id(5)] WaitForAnnIndexRequest? Wait,
    [property: global::Orleans.Id(6)] ApproximateSearchRequest? Approximate,
    [property: global::Orleans.Id(8)] ResourceDefinition Definition,
    [property: global::Orleans.Id(9)] ResourceLifetimeCoverage Coverage,
    [property: global::Orleans.Id(10)] PrincipalRecord Principal,
    [property: global::Orleans.Id(11)] ImmutableArray<ResourceDefinition> Definitions);
