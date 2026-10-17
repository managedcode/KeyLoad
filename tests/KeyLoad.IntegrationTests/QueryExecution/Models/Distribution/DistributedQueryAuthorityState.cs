namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record DistributedQueryAuthorityState(RemotePartitionQueryRf3Seed Seed)
{
    internal long SourceSchema
    {
        get;
        set;
    } = DistributedQueryAuthorityProtocol.InitialSchema;
    internal long DestinationEpoch
    {
        get;
        set;
    } = DistributedQueryAuthorityProtocol.InitialEpoch;
}
