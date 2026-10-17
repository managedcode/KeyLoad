using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record PostgresPureReadRf3State(McpPersistedIdentity Identity, PrincipalRecord Principal,
    PostgresCompositionRf3Image Image);
