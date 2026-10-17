using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record PostgresClosedCommandRf3State(McpPersistedIdentity Admin,
    McpPersistedIdentity Target, PrincipalRecord Denied, PrincipalRecord Repaired,
    Guid CommandId, PostgresCompositionRf3Image Image);
