using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

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

internal sealed record DistributedQueryAuthorityContext(KeyLoadClient Source, KeyLoadClient Destination,
    KeyLoadClient Reader, McpOfficialClient Official, McpOfficialClient SourceOfficial, McpOfficialClient DestinationOfficial);
