using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TransferredAtomicScopeCommands
{
    internal const string RefusedRow = "kl069-transfer-refused";
    internal const string RefusedEvent = "kl069-transfer-event";
    internal const string RefusedMessage = "kl069-transfer-message";
    internal const string UniqueJson = "{\"key\":\"kl069-transfer-refused\",\"title\":\"alpha\",\"count\":3}";
    internal const string UniqueDetail = "A partition-scoped unique index value is already present.";
    internal static CommandRequest Create(PartitionMovementPublicParentRf3Seed seed)
        => seed.Models.Command(new AppendEvents(RelationalSqlRf3Tokens.Streams, RelationalSqlRf3Tokens.StreamId,
            [new(RefusedEvent, RelationalSqlRf3Tokens.EventType, "{}")], new(ExpectedStreamState.Exact, 1)),
            new EnqueueMessage(RelationalSqlRf3Tokens.Queue, RefusedMessage, "{}"),
            new PutDocument(RelationalSqlRf3Tokens.Table, RefusedRow, UniqueJson, 0));
}
