using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(AnnProjectionNativeProtocol.CallAlias)]
internal sealed record AnnProjectionNativeCall(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] string PrincipalId,
    [property: global::Orleans.Id(2)] AnnProjectionObservationRequest Observation,
    [property: global::Orleans.Id(3)] DateTimeOffset ExpiresAt);

internal static class AnnProjectionNativeProtocol
{
    internal const string CallAlias = "keyload.server.ann-projection-observation.call.v1";
    internal const int RequiredVoters = 3;
    internal const int FirstVoter = 0;
    internal const int EmptyBytes = 0;
}
