using System.Text.Json;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal static class QueueReferenceSelectProtocol
{
    private const string MinimumFieldKey = "minimum";
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
    internal const string Prefix = "SELECT TOP (10) q.id AS message_id, d.name AS customer_name, q.payload.score AS score FROM ";
    internal const string Suffix = " AS q INNER JOIN queuecustomers AS d ON q.payload.[to] = d.entity_ref "
        + "WHERE q.payload.score >= @minimum ORDER BY q.id ASC";
    internal static string Sql(string queue = Queue) => Prefix + "QUEUE_MESSAGES('" + queue + "')" + Suffix;

    internal static QueryRequest Request(PartitionRef partition, string queue = Queue)
        => new(partition, Sql(queue), new()
        {
            [MinimumFieldKey] = JsonSerializer.SerializeToElement(7)
        },
            AllowFullScan: true, QueryDialectVersion: SqlOperationProtocol.DefaultQueryDialectVersion);

    internal static SqlOperationRequest Operation(PartitionRef partition, string queue = Queue)
        => new(partition, Sql(queue), new()
        {
            [MinimumFieldKey] = JsonSerializer.SerializeToElement(7)
        },
            AllowFullScan: true, QueryDialectVersion: SqlOperationProtocol.DefaultQueryDialectVersion);

    internal static string Payload(PartitionRef partition, string id, int score)
        => JsonSerializer.Serialize(new
        {
            to = new EntityRef(partition, Target, id),
            score
        }, JsonDefaults.Options);
}
