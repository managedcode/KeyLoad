namespace KeyLoad.Clustering;

[global::Orleans.Alias(ResourceOwnerInventoryReadContract.OperationAlias)]
internal enum ResourceOwnerInventoryReadOperation
{
    CaptureControl = 0,
    ReadPage = 1,
    ValidateControl = 2,
    ValidateOwner = 3
}
