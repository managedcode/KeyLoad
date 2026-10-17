using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record DistributedQueryAuthorityContext(KeyLoadClient Source, KeyLoadClient Destination,
    KeyLoadClient Reader, McpOfficialClient Official, McpOfficialClient SourceOfficial, McpOfficialClient DestinationOfficial);
