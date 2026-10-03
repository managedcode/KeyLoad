using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

/// <summary>Seeds one real partition with fixed-history event and ready-queue model rows.</summary>
internal sealed record SqlModelViewRf3Scenario(PartitionRef Partition)
{
    internal const string StreamSet = "model-events";
    internal const string StreamId = "order-history";
    internal const string Queue = "model-jobs";
    internal const string FirstEventId = "event-created";
    internal const string SecondEventId = "event-paid";
    internal const string FirstMessageId = "message-created";
    internal const string SecondMessageId = "message-paid";
    internal const string EventCanary = "private-event-canary";
    internal const string EventHeaderCanary = "private-event-header-canary";
    internal const string QueueCanary = "private-queue-canary";
    internal const string QueueHeaderCanary = "private-queue-header-canary";
    internal const string SecretPath = "/secret";
    internal const string HeaderSecretPath = "/privateHeader";
    internal const string SecretGrant = "model.secret.read";
    internal const string SecretUseGrant = "model.secret.use";
    internal const int RowLimit = 3;
    internal const long EventGeneration = 1;
    internal static readonly DateTimeOffset HistoricalAt = new(2020, 10, 1, 9, 30, 0, TimeSpan.Zero);
    internal static readonly DateTimeOffset NotBefore = new(2020, 10, 1, 10, 0, 0, TimeSpan.Zero);

    internal StreamRef Stream => new(Partition, StreamSet, StreamId, EventGeneration);
    internal QueueLaneRef Lane => new(Partition, Queue);

    internal static async Task<SqlModelViewRf3Scenario> CreateAsync(KeyLoadClient administrator,
        CancellationToken cancellationToken)
    {
        var partition = new PartitionRef("sql-model-tenant-" + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat),
            "sql-model-database", "sql-model-domain", Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        var protectedField = new SensitiveFieldPolicy(SecretPath, "restricted", SecretGrant, SecretUseGrant);
        var protectedHeader = new SensitiveFieldPolicy(HeaderSecretPath, "restricted", SecretGrant, SecretUseGrant);
        foreach (var (definition, commandId) in new[]
        {
            (new ResourceDefinition(StreamSet, ResourceKind.StreamSet, partition.TransactionDomainId)
            {
                FieldPolicies = [protectedField],
                HeaderPolicies = [protectedHeader]
            }, Guid.NewGuid()),
            (new ResourceDefinition(Queue, ResourceKind.WorkQueue, partition.TransactionDomainId)
            {
                FieldPolicies = [protectedField],
                HeaderPolicies = [protectedHeader]
            }, Guid.NewGuid())
        })
        {
            await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(commandId,
                new(partition.TenantId, partition.DatabaseId, definition), cancellationToken));
        }

        var command = new CommandRequest(Guid.NewGuid(), partition,
        [
            new AppendEvents(StreamSet, StreamId,
            [
                new(FirstEventId, "OrderCreated", "{\"secret\":\"private-event-canary\",\"value\":\"created\"}",
                    "{\"privateHeader\":\"private-event-header-canary\",\"source\":\"orders\"}", 1, HistoricalAt),
                new(SecondEventId, "OrderPaid", "{\"secret\":\"private-event-canary\",\"value\":\"paid\"}",
                    "{\"privateHeader\":\"private-event-header-canary\",\"source\":\"billing\"}", 1, HistoricalAt)
            ], ExpectedStreamRevision.NoStream, EventGeneration),
            new EnqueueMessage(Queue, FirstMessageId,
                "{\"secret\":\"private-queue-canary\",\"value\":\"created\"}",
                "{\"privateHeader\":\"private-queue-header-canary\",\"source\":\"orders\"}", NotBefore, null),
            new EnqueueMessage(Queue, SecondMessageId,
                "{\"secret\":\"private-queue-canary\",\"value\":\"paid\"}",
                "{\"privateHeader\":\"private-queue-header-canary\",\"source\":\"billing\"}", NotBefore, null)
        ]);
        await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(command, cancellationToken));
        return new(partition);
    }

    internal QueryRequest EventSql(long generation = EventGeneration, string? cursor = null)
        => new(Partition, $"SELECT * FROM EVENTS('{StreamSet}','{StreamId}', {generation}) ORDER BY revision LIMIT {RowLimit}",
            AllowFullScan: true, Cursor: cursor);

    internal QueryRequest QueueSql(string? cursor = null)
        => new(Partition, $"SELECT * FROM QUEUE_MESSAGES('{Queue}') ORDER BY id LIMIT {RowLimit}",
            AllowFullScan: true, Cursor: cursor);

    internal AstQueryRequest EventAst(long generation = EventGeneration, string? cursor = null)
        => Ast(ModelQuerySourceKind.Events, StreamSet, StreamId, generation, cursor);

    internal AstQueryRequest QueueAst(string? cursor = null)
        => Ast(ModelQuerySourceKind.QueueMessages, Queue, Queue, 1, cursor);

    internal InspectMessageRequest Inspect(string id) => new(Lane, id);

    internal static ImmutableArray<EventData> ExpectedEvents =>
    [
        new(FirstEventId, "OrderCreated", "{\"secret\":\"private-event-canary\",\"value\":\"created\"}",
            "{\"privateHeader\":\"private-event-header-canary\",\"source\":\"orders\"}", 1, HistoricalAt),
        new(SecondEventId, "OrderPaid", "{\"secret\":\"private-event-canary\",\"value\":\"paid\"}",
            "{\"privateHeader\":\"private-event-header-canary\",\"source\":\"billing\"}", 1, HistoricalAt)
    ];

    private AstQueryRequest Ast(ModelQuerySourceKind kind, string collection, string item, long generation,
        string? cursor)
        => new(Partition,
            new SelectQuery(collection, null, [new("*", "*")], null,
                [new(kind == ModelQuerySourceKind.Events ? "revision" : "id", false)],
                RowLimit, false, new(kind, item, generation)),
            AllowFullScan: true, Cursor: cursor);
}
