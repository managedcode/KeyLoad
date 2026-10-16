namespace KeyLoad.Clustering;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ResourceOwnerInventoryReadContract.RequestAlias)]
internal sealed record ResourceOwnerInventoryReadRequestV1(
    [property: global::Orleans.Id(ResourceOwnerInventoryReadContract.VersionField)] int Version,
    [property: global::Orleans.Id(ResourceOwnerInventoryReadContract.OperationField)] ResourceOwnerInventoryReadOperation Operation,
    [property: global::Orleans.Id(ResourceOwnerInventoryReadContract.RequestField)] ResourceOwnerInventoryRequestV1 Request,
    [property: global::Orleans.Id(ResourceOwnerInventoryReadContract.ControlField)] ResourceOwnerInventoryControlV1? Control,
    [property: global::Orleans.Id(ResourceOwnerInventoryReadContract.ObservedOwnerField)] RegisteredPhysicalOwnerV1 ObservedOwner,
    [property: global::Orleans.Id(ResourceOwnerInventoryReadContract.ContinuationField)] ResourceOwnerInventoryCursorV1? Continuation,
    [property: global::Orleans.Id(ResourceOwnerInventoryReadContract.OwnerCutField)] ResourceOwnerInventoryOwnerCutV1? OwnerCut,
    [property: global::Orleans.Id(ResourceOwnerInventoryReadContract.DestinationVoterField)] string DestinationVoter,
    [property: global::Orleans.Id(ResourceOwnerInventoryReadContract.DestinationSiloAddressField)] string DestinationSiloAddress,
    [property: global::Orleans.Id(ResourceOwnerInventoryReadContract.OriginalPolicyEpochField)] long OriginalPolicyEpoch,
    [property: global::Orleans.Id(ResourceOwnerInventoryReadContract.MaximumReadBytesField)] long MaximumReadBytes,
    [property: global::Orleans.Id(ResourceOwnerInventoryReadContract.MaximumExaminedRecordsField)] int MaximumExaminedRecords,
    [property: global::Orleans.Id(ResourceOwnerInventoryReadContract.MaximumResultBytesField)] int MaximumResultBytes);
