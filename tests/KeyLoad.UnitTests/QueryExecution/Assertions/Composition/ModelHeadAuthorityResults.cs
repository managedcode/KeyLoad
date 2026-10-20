using System.Text.Json;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class ModelHeadAuthorityResults
{
    internal static QueryRow[] Rows(bool queue)
        => queue ? [Queue()] : [Event("created", "Created", "first", 1L), Event("paid", "Paid", "second", 2L)];
    private static QueryRow Event(string id, string kind, string visible, long revision)
        => Row(id, revision, JsonSerializer.Serialize(new
        {
            eventId = id,
            eventType = kind,
            schemaVersion = 1,
            revision,
            eventSequence = revision,
            recordedAt = JsonSerializer.SerializeToElement(ModelHeadAuthorityFixture.Epoch, JsonDefaults.Options).GetString(),
            payload = new
            {
                visible
            },
            headers = new
            {
                trace = "source"
            }
        }, JsonDefaults.Options));
    private static QueryRow Queue() => Row("ready", 1L, JsonSerializer.Serialize(new
    {
        id = "ready",
        state = "Ready",
        attempts = 0,
        stateVersion = 1L,
        notBefore = JsonSerializer.SerializeToElement(ModelHeadAuthorityFixture.Epoch, JsonDefaults.Options).GetString(),
        expiresAt = (DateTimeOffset?)null,
        payload = new
        {
            visible = "queue"
        },
        headers = new
        {
            trace = "source"
        }
    }, JsonDefaults.Options));
    private static QueryRow Row(string id, long revision, string json)
        => new(id, revision, json, true, ["/payload/secret", "/headers/privateHeader"]);
    internal static string Path(bool queue) => queue ? "model-scan:queue-messages" : "model-scan:events";
}
