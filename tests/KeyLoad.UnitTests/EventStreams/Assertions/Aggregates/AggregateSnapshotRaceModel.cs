using KeyLoad.Client;

namespace KeyLoad.UnitTests.EventStreams;

internal static class AggregateSnapshotRaceModel
{
    internal static async Task RequireAsync(AggregateSnapshotRaceFixture fixture, bool healthy)
    {
        var command = (healthy ? fixture.Healthy : fixture.Winner)
            ?? throw new InvalidOperationException("The owned snapshot producer is unavailable.");
        var mutation = (StoreAggregateSnapshot)command.Mutations.Single();
        var stream = fixture.Stream;
        var snapshot = new AggregateSnapshotState(stream, healthy ? 2L : 1L, mutation.SourceRevision,
            AggregateSnapshotRaceFixture.Reducer, 1, mutation.StateJson, string.Empty);
        snapshot = snapshot with
        {
            Checksum = JsonData.Fingerprint(snapshot)
        };
        EventRecord[] records = [Record(stream, "first", 1), Record(stream, "second", 2), Record(stream, "third", 3)];
        var page = fixture.Read();
        await AggregateSnapshotRaceValues.EqualAsync(page, new AggregateReplayPage(stream, new(3, 1, 1), snapshot,
            [.. records.Where(record => record.Revision > mutation.SourceRevision)], fixture.Store.Position));
        var full = fixture.Read(true);
        await AggregateSnapshotRaceValues.EqualAsync(full, new AggregateReplayPage(stream, new(3, 1, 1), null,
            [.. records], fixture.Store.Position));
        var reducer = AggregateReplayWorkerTestSupport.CounterReducer();
        await Assert.That(AggregateReplayReduction.Reduce(page, reducer, UnitClientOptions.Replay())).IsEqualTo("{\"total\":6}");
        await Assert.That(AggregateReplayReduction.Reduce(full, reducer, UnitClientOptions.Replay())).IsEqualTo("{\"total\":6}");
        var callbacks = 0;
        var incompatible = reducer with
        {
            EventSchemaVersion = 2,
            Apply = (state, _) => { callbacks++; return state; }
        };
        var failure = Assert.ThrowsExactly<InvalidDataException>(() =>
            AggregateReplayReduction.Reduce(full, incompatible, UnitClientOptions.Replay()));
        await Assert.That(failure.Message).IsEqualTo("The event schema does not match the reducer.");
        await Assert.That(callbacks).IsEqualTo(0);
        await AggregateSnapshotRaceValues.OriginalsAsync(fixture);
    }
    private static EventRecord Record(StreamRef stream, string id, int amount) => new(stream, amount, amount,
        new(id, "CounterChanged", "{\"increment\":" + amount.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ",\"secret\":\"PRIVATE-RACE-PAYLOAD\"}", "{\"privateHeader\":\"PRIVATE-RACE-HEADER\"}"),
        AggregateSnapshotRaceFixture.Epoch);
}
