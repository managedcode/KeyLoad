using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.Search;

[Orleans.GenerateSerializer, Orleans.Alias(AnnProjectionNativeProtocol.CallAlias)]
internal sealed record AnnProjectionNativeCall(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] string PrincipalId,
    [property: Orleans.Id(2)] AnnProjectionObservationRequest Observation,
    [property: Orleans.Id(3)] DateTimeOffset ExpiresAt);
