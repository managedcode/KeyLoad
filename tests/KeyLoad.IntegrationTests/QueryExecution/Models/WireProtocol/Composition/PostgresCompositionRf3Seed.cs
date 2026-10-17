using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record PostgresCompositionRf3Seed(McpPersistedIdentity Identity, PrincipalRecord Principal,
    CommandRequest Seed, CommitReceipt SeedReceipt, CommandRequest Denied, PostgresCompositionRf3Image Before);
