namespace KeyLoad.Clustering;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ControlledDocumentReadProtocol.ResultAlias)]
internal sealed record ControlledDocumentReadResult(
    [property: global::Orleans.Id(0)] DocumentResult? Document,
    [property: global::Orleans.Id(1)] long ReadBytes,
    [property: global::Orleans.Id(2)] int ExaminedRecords);
