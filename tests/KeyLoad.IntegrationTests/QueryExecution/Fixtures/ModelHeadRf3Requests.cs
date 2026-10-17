using System.Text.Json;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class ModelHeadRf3Requests
{
    private const string EventFields = "eventId,eventType,schemaVersion,revision,eventSequence,payload.visible AS visible,headers.trace AS trace";
    internal static QueryRequest Sql(ModelHeadRf3State state, bool queue, long generation = 1L)
        => new(state.Partition, queue ? "SELECT TOP (10) * FROM QUEUE_MESSAGES('model-head-queue') ORDER BY id"
            : "SELECT TOP (10) " + EventFields + " FROM EVENTS('model-head-events','source',"
                + generation.ToString(System.Globalization.CultureInfo.InvariantCulture) + ") ORDER BY revision", AllowFullScan: true);
    internal static AstQueryRequest Ast(ModelHeadRf3State state, bool queue, long generation = 1L)
        => new(state.Partition, new(queue ? ModelHeadRf3State.Queue : ModelHeadRf3State.Events, null,
            queue ? [new("*", "*")] : [new("/eventId", "eventId"), new("/eventType", "eventType"),
                new("/schemaVersion", "schemaVersion"), new("/revision", "revision"), new("/eventSequence", "eventSequence"),
                new("/payload/visible", "visible"), new("/headers/trace", "trace")], null,
            [new(queue ? "/id" : "/revision", false)], 10, ModelSource: new(queue ? ModelQuerySourceKind.QueueMessages
                : ModelQuerySourceKind.Events, queue ? ModelHeadRf3State.Queue : ModelHeadRf3State.Stream, generation)), AllowFullScan: true);
    internal static QueryRow[] Rows(bool queue) => queue ? [Row("ready", 1L, JsonSerializer.Serialize(new
    {
        id = "ready", state = "Ready", attempts = 0, stateVersion = 1L, notBefore = ModelHeadRf3State.NotBefore,
        expiresAt = (DateTimeOffset?)null, payload = new { visible = "queue" }, headers = new { trace = "source" }
    }, JsonDefaults.Options))] : [Event("created", "Created", "first", 1L), Event("paid", "Paid", "second", 2L)];
    private static QueryRow Event(string id, string kind, string visible, long revision)
        => Row(id, revision, JsonSerializer.Serialize(new
        {
            eventId = id,
            eventType = kind,
            schemaVersion = 1,
            revision,
            eventSequence = revision,
            visible,
            trace = "source"
        }, JsonDefaults.Options));
    private static QueryRow Row(string id, long revision, string json)
        => new(id, revision, json, true, ["/payload/secret", "/headers/privateHeader"]);
    internal static string Path(bool queue) => queue ? "model-scan:queue-messages" : "model-scan:events";
}
