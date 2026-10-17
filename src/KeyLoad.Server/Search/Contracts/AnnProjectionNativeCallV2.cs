using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server.Features.Search;

[Orleans.GenerateSerializer, Orleans.Alias(AnnProjectionNativeCallV2.ContractAlias)]
internal sealed record AnnProjectionNativeCallV2(
    [property: Orleans.Id(0)] AnnProjectionNativeCall Original,
    [property: Orleans.Id(1)] ResourceDefinition Definition,
    [property: Orleans.Id(2)] ResourceLifetimeCoverageV1 Coverage,
    [property: Orleans.Id(3)] PrincipalRecord Principal,
    [property: Orleans.Id(4)] ImmutableArray<ResourceDefinition> Definitions)
{
    internal const string ContractAlias = "keyload.server.ann-projection-observation.call.v2";
}
