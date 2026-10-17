using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record PostgresPureReadRf3State(McpPersistedIdentity Identity, PrincipalRecord Principal,
    PostgresCompositionRf3Image Image);

internal sealed record PostgresNativeExtendedRf3State(PostgresCompositionRf3State Composition, PostgresPureReadRf3State Reads,
    PostgresClosedCommandRf3State ClosedCommands);
