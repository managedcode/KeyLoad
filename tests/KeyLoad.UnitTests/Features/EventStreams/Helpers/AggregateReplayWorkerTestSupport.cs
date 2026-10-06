using System.Text.Json;
using KeyLoad.Client;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class AggregateReplayWorkerTestSupport
{
    internal const string ReducerVersion = "counter.reducer.v1";
    internal const string EventType = "CounterChanged";
    private const string TotalField = "total";
    private const string IncrementField = "increment";
    private const string PartitionId = "key";

    internal static AggregateReplayReducer CounterReducer(int eventVersion = 1,
        Func<string, EventRecord, string>? apply = null)
        => new(ReducerVersion, 1, eventVersion, "{\"" + TotalField + "\":0}", apply ?? AddIncrement);

    internal static string AddIncrement(string state, EventRecord record)
    {
        using var stateDocument = JsonDocument.Parse(state);
        using var eventDocument = JsonDocument.Parse(record.Data.PayloadJson);
        var total = stateDocument.RootElement.GetProperty(TotalField).GetInt32();
        var increment = eventDocument.RootElement.GetProperty(IncrementField).GetInt32();
        return "{\"" + TotalField + "\":" + (total + increment) + "}";
    }

    internal static int ReadInteger(string json, string property)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty(property).GetInt32();
    }

    internal static StreamRef Stream() => new(new("tenant", "database", "domain", PartitionId), "orders", "aggregate");

    internal static EventRecord Event(StreamRef stream, long revision, string id, string payload, int schema = 1)
        => new(stream, revision, revision, new(id, EventType, payload, "{}", schema), DateTimeOffset.UnixEpoch.AddSeconds(revision));

    internal static AggregateSnapshotState Snapshot(StreamRef stream, long sourceRevision, string state)
        => new(stream, 1, sourceRevision, ReducerVersion, 1, state, "server-owned-checksum");

    internal static AggregateReplayPage Page(StreamRef stream, AggregateSnapshotState? snapshot, EventRecord[] events)
    {
        var tail = events.Length == 0 ? snapshot?.SourceRevision ?? 0 : events[^1].Revision;
        return new(stream, new(tail, 1, stream.Generation), snapshot, [.. events], tail + 10);
    }

}
