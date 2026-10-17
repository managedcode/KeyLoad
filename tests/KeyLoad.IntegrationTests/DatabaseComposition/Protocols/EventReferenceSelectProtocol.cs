using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class EventReferenceSelectProtocol
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
    internal const string Prefix = "SELECT e.eventId AS event_id, d.name AS customer_name, e.payload.score AS score FROM ";
    internal const string Suffix = " AS e INNER JOIN eventcustomers AS d ON e.payload.to = d.entity_ref "
        + "WHERE e.payload.score >= @minimum ORDER BY e.revision ASC LIMIT 10";

    internal static string Sql(string streamSet = StreamSet, long generation = 1)
        => Prefix + "EVENTS('" + streamSet + "','" + Stream + "'," + generation.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")" + Suffix;

    internal static QueryRequest Request(PartitionRef partition, string streamSet = StreamSet, long generation = 1)
        => new(partition, Sql(streamSet, generation), new()
        {
            [MinimumParameter] = JsonSerializer.SerializeToElement(7)
        },
            AllowFullScan: true, QueryDialectVersion: 2);

    internal static string Payload(PartitionRef partition, string id, int score)
        => JsonSerializer.Serialize(new
        {
            to = new EntityRef(partition, Target, id),
            score
        }, JsonDefaults.Options);

    internal static SqlOperationRequest Operation(PartitionRef partition, string streamSet = StreamSet, long generation = 1)
        => new(partition, Sql(streamSet, generation), new()
        {
            [MinimumParameter] = JsonSerializer.SerializeToElement(7)
        },
            AllowFullScan: true, QueryDialectVersion: 2);
}
