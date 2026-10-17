namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record PostgresNativeExtendedRf3State(PostgresCompositionRf3State Composition, PostgresPureReadRf3State Reads,
    PostgresClosedCommandRf3State ClosedCommands);
