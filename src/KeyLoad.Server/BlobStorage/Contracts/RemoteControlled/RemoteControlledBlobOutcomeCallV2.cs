using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server.Features.BlobStorage;

[Orleans.GenerateSerializer, Orleans.Alias(RemoteControlledBlobOutcomeProtocolV2.CallAlias)]
internal sealed record RemoteControlledBlobOutcomeCallV2(
    [property: Orleans.Id(0)] RemoteControlledBlobCall Original,
    [property: Orleans.Id(1)] ResourceLifetimeCoverageV1 Coverage);
