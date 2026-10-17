namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionControlDocumentReadFrameV2.SerializationAlias)]
internal sealed record PartitionControlDocumentReadFrameV2(
    [property: global::Orleans.Id(0)] PartitionControlDocumentReadFrame Original,
    [property: global::Orleans.Id(1)] ResourceLifetimeCoverageV1 Coverage)
{
    internal const string SerializationAlias = "keyload.core.partition-control-document-read-frame.v2";
}
