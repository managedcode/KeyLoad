using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record PostgresRf3BackpressureState(McpPersistedIdentity Identity,
    CommandRequest Command, CommitReceipt Receipt);
