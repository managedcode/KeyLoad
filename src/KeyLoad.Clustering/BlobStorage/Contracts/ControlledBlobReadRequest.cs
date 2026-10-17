using KeyLoad.Core.Features.BlobStorage;

namespace KeyLoad.Clustering;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ControlledBlobReadProtocol.RequestAlias)]
internal sealed record ControlledBlobReadRequest(
    [property: global::Orleans.Id(0)] ControlledBlobReadFrame Frame,
    [property: global::Orleans.Id(1)] long MaximumReadBytes,
    [property: global::Orleans.Id(2)] int MaximumExaminedRecords,
    [property: global::Orleans.Id(3)] int MaximumResultBytes);
