using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.QueryExecution;

[Orleans.GenerateSerializer, Orleans.Alias(RemoteQueryLifetimeRequestV2.ContractAlias)]
internal sealed record RemoteQueryLifetimeRequestV2(
    [property: Orleans.Id(0)] RemoteDocumentCallV1 Original,
    [property: Orleans.Id(1)] ResourceDefinition Definition,
    [property: Orleans.Id(2)] ResourceLifetimeCoverageV1 Coverage,
    [property: Orleans.Id(3)] PrincipalRecord Principal)
{
    internal const string ContractAlias = "keyload.server.remote-query-lifetime-request.v2";
}
