using System.Text.Json;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlQueueReferenceJoinFixture
{
    private const string TargetPrimaryKey = "id";
    private const string MinimumParameter = "minimum";

    internal const string Queue = "joinedqueue";
    internal const string InvalidQueue = "invalidqueue";
    internal const string EmptyQueue = "emptyqueue";
    internal const string Target = "queuecustomers";
    internal const string Reader = "queue-join-reader";
    internal const string RefRead = "reference.read";
    internal const string RefUse = "reference.use";
    internal const string NameRead = "customer.read";
    internal const string FirstJson = "{\"id\":\"c1\",\"name\":\"Ada\"}";
    internal const string HiddenJson = "{\"id\":\"c2\",\"name\":\"Lin\"}";
    internal const string RepairedJson = "{\"id\":\"m\",\"name\":\"Max\"}";
    internal const string Prefix = "SELECT q.id AS message_id, d.name AS customer_name, q.payload.score AS score FROM ";
    internal const string Suffix = " AS q INNER JOIN queuecustomers AS d ON q.payload.to = d.entity_ref "
        + "WHERE q.payload.score >= @minimum ORDER BY q.id ASC LIMIT 10";
    internal static string Sql(string queue = Queue) => Prefix + "QUEUE_MESSAGES('" + queue + "')" + Suffix;

    internal static QueryRequest Request(PartitionRef partition, string queue = Queue)
        => new(partition, Sql(queue), new() { [MinimumParameter] = JsonSerializer.SerializeToElement(7) },
            AllowFullScan: true, QueryDialectVersion: 2);

    internal static AstQueryRequest Ast(PartitionRef partition)
        => new(partition, new SelectQuery(Queue, "q",
            [new("/id", "message_id", "q"), new("/name", "customer_name", "d"), new("/payload/score", "score", "q")],
            new Comparison(new FieldOperand("/payload/score"), ">=", new ParameterOperand(MinimumParameter)),
            [new("/id", false)], 10, ModelSource: new(ModelQuerySourceKind.QueueMessages, Queue, 1),
            InnerJoin: new(Target, "d", "/payload/to", "/entity_ref")),
            new() { [MinimumParameter] = JsonSerializer.SerializeToElement(7) }, AllowFullScan: true, AstVersion: 2);

    internal static QueryRequest FilteredInvalidRequest(PartitionRef partition)
        => Request(partition, InvalidQueue) with
        { Parameters = new() { [MinimumParameter] = global::System.Text.Json.JsonSerializer.SerializeToElement(100) } };

    internal static AstQueryRequest FilteredInvalidAst(PartitionRef partition)
    {
        var request = Ast(partition);
        return request with
        {
            Query = request.Query with
            { Collection = InvalidQueue, ModelSource = new(ModelQuerySourceKind.QueueMessages, InvalidQueue, 1) },
            Parameters = new() { [MinimumParameter] = global::System.Text.Json.JsonSerializer.SerializeToElement(100) }
        };
    }

    internal static string Payload(PartitionRef partition, string id, int score)
        => JsonSerializer.Serialize(new { to = new EntityRef(partition, Target, id), score }, JsonDefaults.Options);

    internal static (CommandRequest Command, CommitReceipt Receipt) Seed(TestDatabase database)
    {
        foreach (var queue in new[] { Queue, InvalidQueue, EmptyQueue })
        {
            database.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(database.Partition.TenantId,
                database.Partition.DatabaseId, new ResourceDefinition(queue, ResourceKind.WorkQueue,
                    database.Partition.TransactionDomainId)
                { FieldPolicies = [new("/to", "reference", RawReadGrant: RefRead, RawUseGrant: RefUse)] }));
        }
        database.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(database.Partition.TenantId,
            database.Partition.DatabaseId, new ResourceDefinition(Target, ResourceKind.Collection, database.Partition.TransactionDomainId)
            {
                RelationalSchema = new(TargetPrimaryKey, [new(TargetPrimaryKey, RelationalColumnType.Text), new("name", RelationalColumnType.Text)]),
                FieldPolicies = [new("/name", "private", RawReadGrant: NameRead)]
            }));
        var command = new CommandRequest(Guid.NewGuid(), database.Partition,
        [
            new PutDocument(Target, "c1", FirstJson, Access: new(OwnerId: Reader)),
            new PutDocument(Target, "c2", HiddenJson, Access: new(OwnerId: "other-owner")),
            new EnqueueMessage(Queue, "a", Payload(database.Partition, "c1", 7)),
            new EnqueueMessage(Queue, "b", Payload(database.Partition, "m", 7)),
            new EnqueueMessage(Queue, "c", Payload(database.Partition, "c2", 7)),
            new EnqueueMessage(Queue, "d", Payload(database.Partition, "c1", 2)),
            new EnqueueMessage(Queue, "e", "{\"score\":7}"),
            new EnqueueMessage(InvalidQueue, "wrong", Payload(database.Partition with { PartitionKey = "foreign" }, "c1", 7))
        ]);
        return (command, database.Submit(OperationKind.Batch, command, id: command.CommandId).Get<CommitReceipt>());
    }
}
