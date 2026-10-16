namespace KeyLoad.Clustering;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ResourceOwnerInventoryReadContract.ReplyAlias)]
internal sealed record ResourceOwnerInventoryReadReplyV1(
    [property: global::Orleans.Id(ResourceOwnerInventoryReadContract.ReplyControlField)] ResourceOwnerInventoryControlV1? Control,
    [property: global::Orleans.Id(ResourceOwnerInventoryReadContract.ReplyPageField)] ResourceOwnerInventoryPageV1? Page,
    [property: global::Orleans.Id(ResourceOwnerInventoryReadContract.ValidatedField)] bool Validated,
    [property: global::Orleans.Id(ResourceOwnerInventoryReadContract.ReadBytesField)] long ReadBytes,
    [property: global::Orleans.Id(ResourceOwnerInventoryReadContract.ExaminedRecordsField)] int ExaminedRecords);
