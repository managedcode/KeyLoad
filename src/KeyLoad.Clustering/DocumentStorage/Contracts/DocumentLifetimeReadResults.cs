namespace KeyLoad.Clustering;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(DocumentLifetimeReadScope.OwnedResultAlias)]
internal sealed record OwnedDocumentReadResultV2(
    [property: global::Orleans.Id(0)] OwnedDocumentReadResultV1 Result,
    [property: global::Orleans.Id(1)] long ReadBytes,
    [property: global::Orleans.Id(2)] int ExaminedRecords);

[global::Orleans.GenerateSerializer, global::Orleans.Alias(DocumentLifetimeReadScope.ControlledResultAlias)]
internal sealed record ControlledDocumentReadResultV2(
    [property: global::Orleans.Id(0)] DocumentResult? Document,
    [property: global::Orleans.Id(1)] long ReadBytes,
    [property: global::Orleans.Id(2)] int ExaminedRecords);
