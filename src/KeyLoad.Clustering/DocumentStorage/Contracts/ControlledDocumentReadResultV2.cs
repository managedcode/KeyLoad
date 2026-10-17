namespace KeyLoad.Clustering;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(DocumentLifetimeReadScope.ControlledResultAlias)]
internal sealed record ControlledDocumentReadResultV2(
    [property: global::Orleans.Id(0)] DocumentResult? Document,
    [property: global::Orleans.Id(1)] long ReadBytes,
    [property: global::Orleans.Id(2)] int ExaminedRecords);
