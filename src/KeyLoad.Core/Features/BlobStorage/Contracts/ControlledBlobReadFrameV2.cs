using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.BlobStorage;

[Orleans.GenerateSerializer, Orleans.Alias(ControlledBlobLifetimeReadProtocol.FrameAlias)]
internal sealed record ControlledBlobReadFrameV2(
    [property: Orleans.Id(0)] ControlledBlobReadFrame Original,
    [property: Orleans.Id(1)] ResourceLifetimeCoverageV1 Coverage);
