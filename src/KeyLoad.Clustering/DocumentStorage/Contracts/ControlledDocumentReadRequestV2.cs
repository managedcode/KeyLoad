using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Clustering;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(DocumentLifetimeReadScope.ControlledRequestAlias)]
internal sealed record ControlledDocumentReadRequestV2(
    [property: global::Orleans.Id(0)] PartitionControlDocumentReadFrameV2 Frame,
    [property: global::Orleans.Id(1)] long MaximumReadBytes,
    [property: global::Orleans.Id(2)] int MaximumExaminedRecords,
    [property: global::Orleans.Id(3)] int MaximumResultBytes);
