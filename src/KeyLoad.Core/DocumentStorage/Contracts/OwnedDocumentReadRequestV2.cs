namespace KeyLoad.Core.Features.DocumentStorage;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(OwnedDocumentReadRequestV2.SerializationAlias)]
internal sealed record OwnedDocumentReadRequestV2(
    [property: global::Orleans.Id(0)] RemoteDocumentReadFrameV2 Frame,
    [property: global::Orleans.Id(1)] GetDocumentRequest Request,
    [property: global::Orleans.Id(2)] PhysicalShardRecord Owner,
    [property: global::Orleans.Id(3)] long MaximumReadBytes,
    [property: global::Orleans.Id(4)] int MaximumExaminedRecords,
    [property: global::Orleans.Id(5)] int MaximumResultBytes)
{
    internal const string SerializationAlias = "keyload.core.owned-document-read-request.v2";
}
