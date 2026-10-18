namespace KeyLoad.Server.Features.DocumentStorage;

[Orleans.Alias(RemoteResourceLifetimeReadProtocol.KindAlias)]
internal enum RemoteResourceLifetimeReadKindV2
{
    Document = 0,
    PartitionQuery = 3,
    DistributedSearch = 4
}
