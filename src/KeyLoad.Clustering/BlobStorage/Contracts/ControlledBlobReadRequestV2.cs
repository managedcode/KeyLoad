using KeyLoad.Core.Features.BlobStorage;

namespace KeyLoad.Clustering;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ControlledBlobLifetimeReadProtocol.RequestAlias)]
internal sealed record ControlledBlobReadRequestV2(
    [property: global::Orleans.Id(0)] ControlledBlobReadFrameV2 Frame,
    [property: global::Orleans.Id(1)] long MaximumReadBytes,
    [property: global::Orleans.Id(2)] int MaximumExaminedRecords,
    [property: global::Orleans.Id(3)] int MaximumResultBytes);
