using System.Text.Json;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlEventReferenceJoinFixture
{
    internal const string MinimumParameter = "minimum";
    internal const string PrimaryKeyColumn = "id";
    internal const string StreamSet = "joinedevents";
    internal const string InvalidStreamSet = "invalidevents";
    internal const string EmptyStreamSet = "emptyevents";
    internal const string Stream = "source";
    internal const string Target = "eventcustomers";
    internal const string Reader = "event-join-reader";
    internal const string RefRead = "reference.read";
    internal const string RefUse = "reference.use";
    internal const string NameRead = "customer.read";
    internal const string FirstJson = "{\"id\":\"c1\",\"name\":\"Ada\"}";
    internal const string HiddenJson = "{\"id\":\"c2\",\"name\":\"Lin\"}";
    internal const string RepairedJson = "{\"id\":\"m\",\"name\":\"Max\"}";
    internal const string Prefix = "SELECT TOP (10) e.eventId AS event_id, d.name AS customer_name, e.payload.score AS score FROM ";
    internal const string Suffix = " AS e INNER JOIN eventcustomers AS d ON e.payload.[to] = d.entity_ref "
        + "WHERE e.payload.score >= @minimum ORDER BY e.revision ASC";

    internal static string Sql(string streamSet = StreamSet, long generation = 1)
        => Prefix + "EVENTS('" + streamSet + "','" + Stream + "'," + generation.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")" + Suffix;

    internal static QueryRequest Request(PartitionRef partition, string streamSet = StreamSet, long generation = 1)
        => new(partition, Sql(streamSet, generation), new()
        {
            [MinimumParameter] = JsonSerializer.SerializeToElement(7)
        },
            AllowFullScan: true, QueryDialectVersion: SqlOperationProtocol.DefaultQueryDialectVersion);

    internal static AstQueryRequest Ast(PartitionRef partition)
        => new(partition, new SelectQuery(StreamSet, "e",
            [new("/eventId", "event_id", "e"), new("/name", "customer_name", "d"), new("/payload/score", "score", "e")],
            new Comparison(new FieldOperand("/payload/score"), ">=", new ParameterOperand(MinimumParameter)),
            [new("/revision", false)], 10, ModelSource: new(ModelQuerySourceKind.Events, Stream, 1),
            InnerJoin: new(Target, "d", "/payload/to", "/entity_ref")),
            new()
            {
                [MinimumParameter] = JsonSerializer.SerializeToElement(7)
            }, AllowFullScan: true, AstVersion: SqlOperationProtocol.DefaultQueryDialectVersion);

    internal static string Payload(PartitionRef partition, string id, int score)
        => JsonSerializer.Serialize(new
        {
            to = new EntityRef(partition, Target, id),
            score
        }, JsonDefaults.Options);

    internal static (CommandRequest Command, CommitReceipt Receipt) Seed(TestDatabase database)
    {
        foreach (var streamSet in new[] { StreamSet, InvalidStreamSet, EmptyStreamSet })
        {
            database.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(database.Partition.TenantId,
                database.Partition.DatabaseId, new ResourceDefinition(streamSet, ResourceKind.StreamSet,
                    database.Partition.TransactionDomainId)
                {
                    FieldPolicies = [new("/to", "reference", RawReadGrant: RefRead, RawUseGrant: RefUse)]
                }));
        }
        database.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(database.Partition.TenantId,
            database.Partition.DatabaseId, new ResourceDefinition(Target, ResourceKind.Collection, database.Partition.TransactionDomainId)
            {
                RelationalSchema = new(PrimaryKeyColumn, [new(PrimaryKeyColumn, RelationalColumnType.Text), new("name", RelationalColumnType.Text)]),
                FieldPolicies = [new("/name", "private", RawReadGrant: NameRead)]
            }));
        var command = new CommandRequest(Guid.NewGuid(), database.Partition,
        [
            new PutDocument(Target, "c1", FirstJson, Access: new(OwnerId: Reader)),
            new PutDocument(Target, "c2", HiddenJson, Access: new(OwnerId: "other-owner")),
            new AppendEvents(StreamSet, Stream,
                [new("z", "linked", Payload(database.Partition, "c1", 7)),
                 new("a", "linked", Payload(database.Partition, "m", 7)),
                 new("m", "linked", Payload(database.Partition, "c2", 7)),
                 new("b", "linked", Payload(database.Partition, "c1", 2)),
                 new("n", "unlinked", "{\"score\":7}")], ExpectedStreamRevision.NoStream),
            new AppendEvents(InvalidStreamSet, Stream,
                [new("wrong", "linked", Payload(database.Partition with { PartitionKey = "foreign" }, "c1", 7))],
                ExpectedStreamRevision.NoStream)
        ]);
        return (command, database.Submit(OperationKind.Batch, command, id: command.CommandId).Get<CommitReceipt>());
    }
}
