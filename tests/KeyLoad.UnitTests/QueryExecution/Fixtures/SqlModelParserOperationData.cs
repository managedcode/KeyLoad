using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlModelParserOperationData
{
    internal const string Root = "root";
    internal const string Reader = "parser-model-reader";
    internal const string SecondEvent = "event-b";
    internal const string SecondMessage = "message-b";
    internal const string EventSql = "SELECT TOP (4) e.eventType FROM EVENTS('events', 'stream-a', 1) e WHERE e.payload.visible = 'event-visible' ORDER BY e.revision DESC";
    internal const string StaleSql = "SELECT TOP (4) e.eventType FROM EVENTS('events', 'stream-a', 3) e WHERE e.payload.visible = 'event-visible' ORDER BY e.revision DESC";
    internal const string QueueSql = "SELECT q.id, q.state FROM QUEUE_MESSAGES('jobs') q ORDER BY q.id DESC";
    internal const string StaleDetail = "The event source generation is stale.";
    internal const string EventPath = "model-scan:events";
    internal const string QueuePath = "model-scan:queue-messages";
    internal const int Limit = 4;
    internal const long CurrentGeneration = 1;
    internal const long StaleGeneration = 3;

    internal static SelectQuery Events(long generation = CurrentGeneration)
        => new(SqlModelViewTestSupport.StreamSet, "e", [new("/eventType", "eventType")],
            new Comparison(new FieldOperand("/payload/visible"), "=", ValueOperand.Create(
                System.Text.Json.JsonSerializer.SerializeToElement("event-visible"))),
            [new("/revision", true)], Limit,
            ModelSource: new(ModelQuerySourceKind.Events, SqlModelViewTestSupport.StreamId, generation));
    internal static SelectQuery Queue => new(SqlModelViewTestSupport.Queue, "q", [new("/id", "id"), new("/state", "state")],
        null, [new("/id", true)], Limit, ModelSource: new(ModelQuerySourceKind.QueueMessages, SqlModelViewTestSupport.Queue));
}
