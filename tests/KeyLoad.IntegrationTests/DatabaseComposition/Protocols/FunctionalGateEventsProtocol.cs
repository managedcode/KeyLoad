using System.Globalization;
using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateEventsProtocol
{
    internal const int ResourceCount = 3;
    internal const string Reducer = "functional-gate-events-v1";
    internal const string EventIdColumn = "event_id";
    internal const string IterationField = "iteration";
    internal const string TargetIdColumn = "target_id";

    internal static string Resource(int index) => "gate_events_" + index.ToString(CultureInfo.InvariantCulture);

    internal static StreamRef Stream(FunctionalGateContext context, int iteration, int resource)
        => new(context.Partition, Resource(resource), iteration < 0 ? "baseline" : "iteration-" + iteration.ToString(CultureInfo.InvariantCulture));

    internal static EventData Event(FunctionalGateContext context, int iteration, bool linked)
    {
        var suffix = iteration.ToString(CultureInfo.InvariantCulture);
        var kind = linked ? "linked" : "unlinked";
        var payload = JsonSerializer.Serialize(new
        {
            to = linked ? context.Bind(context.Anchor) : null,
            previous = linked ? null : context.Bind(context.Anchor),
            iteration,
            linked
        }, JsonDefaults.Options);
        return new("event-" + suffix + "-" + kind, kind, payload,
            JsonSerializer.Serialize(new
            {
                module = "events",
                iteration
            }, JsonDefaults.Options),
            SchemaVersion: 1, OccurredAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            CorrelationId: "correlation-" + suffix, CausationId: "cause-" + suffix);
    }

    internal static CommandRequest Append(FunctionalGateContext context, StreamRef stream, EventData data, long revision)
        => new(Guid.NewGuid(), context.Partition,
            [new AppendEvents(stream.StreamSet, stream.StreamId, [data], revision == 0
                ? ExpectedStreamRevision.NoStream : ExpectedStreamRevision.Exact(revision))]);

    internal static QueryRequest Query(StreamRef stream, int iteration)
        => new(stream.Partition,
            "SELECT TOP (10) e.eventId AS event_id, e.payload.iteration AS iteration, d.id AS target_id FROM EVENTS('"
            + stream.StreamSet + "','" + stream.StreamId + "',1) AS e INNER JOIN gate_docs_0 AS d "
            + "ON e.payload.[to] = d.entity_ref WHERE e.payload.iteration = @iteration ORDER BY e.revision ASC",
            new()
            {
                [IterationField] = JsonSerializer.SerializeToElement(iteration)
            },
            AllowFullScan: true, QueryDialectVersion: 2);
}
