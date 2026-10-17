namespace KeyLoad.Server.Features.DocumentStorage;

[Orleans.Alias(RemoteResourceLifetimeReadProtocol.KindAlias)]
internal enum RemoteResourceLifetimeReadKindV2
{
    Document = 0,
    ControlledDocument = 1,
    ControlledBlob = 2,
    PartitionQuery = 3,
    DistributedSearch = 4
}
