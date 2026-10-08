using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Orleans;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ControlledDocumentReadProtocol.RequestAlias)]
internal sealed record ControlledDocumentReadRequest(
    [property: global::Orleans.Id(0)] PartitionControlDocumentReadFrame Frame,
    [property: global::Orleans.Id(1)] long MaximumReadBytes,
    [property: global::Orleans.Id(2)] int MaximumExaminedRecords,
    [property: global::Orleans.Id(3)] int MaximumResultBytes);

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ControlledDocumentReadProtocol.ResultAlias)]
internal sealed record ControlledDocumentReadResult(
    [property: global::Orleans.Id(0)] DocumentResult? Document,
    [property: global::Orleans.Id(1)] long ReadBytes,
    [property: global::Orleans.Id(2)] int ExaminedRecords);
