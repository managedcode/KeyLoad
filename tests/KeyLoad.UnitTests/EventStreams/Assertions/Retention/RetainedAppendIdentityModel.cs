using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class RetainedAppendIdentityModel
{
    internal static async Task RequireAsync(DatabaseEngine database, ZoneTreeStore store,
        RetainedAppendIdentityFixture fixture, bool healthy)
    {
        var stream = fixture.Stream;
        EventRecord[] records = [new(stream, 1, 1, RetainedAppendIdentityFixture.Data("initial"), RetainedAppendIdentityFixture.Epoch),
            new(stream, 2, 3, RetainedAppendIdentityFixture.Data(RetainedAppendIdentityFixture.Candidate), RetainedAppendIdentityFixture.Epoch)];
        if (healthy)
        {
            records = [.. records, new(stream, 3, 4, RetainedAppendIdentityFixture.Data("following"), RetainedAppendIdentityFixture.Epoch)];
        }
        await RetainedAppendIdentityAssertions.SameAsync(database.ReadStream("root", new ReadStreamRequest(stream)),
            new StreamPage(stream, new(records.Length, 1, 1), [.. records], database.LastApplied, false, null, database.LastApplied));
        var foreign = stream with
        {
            StreamId = RetainedAppendIdentityFixture.Foreign
        };
        await RetainedAppendIdentityAssertions.SameAsync(database.ReadStream("root", new ReadStreamRequest(foreign)),
            new StreamPage(foreign, new(1, 1, 1), [new(foreign, 1, 2,
                RetainedAppendIdentityFixture.Data(RetainedAppendIdentityFixture.Candidate), RetainedAppendIdentityFixture.Epoch)],
                database.LastApplied, false, null, database.LastApplied));
        foreach (var id in healthy ? new[] { "repaired", "healthy" } : new[] { "repaired" })
        {
            await RetainedAppendIdentityAssertions.SameAsync(database.GetDocument("root", new(stream.Partition,
                RetainedAppendIdentityFixture.Documents, id)), new DocumentResult(fixture.Harness.ExpectedReference(new(stream.Partition,
                    RetainedAppendIdentityFixture.Documents, id)), 1, "{\"producer\":true}", false, []));
            var sequence = id == "repaired" ? 1L : 2L;
            await RetainedAppendIdentityAssertions.SameAsync(database.InspectMessage("root",
                new(stream.Partition, RetainedAppendIdentityFixture.Queue), id), new MessageInspection(
                    new(id, MessageState.Ready, 0, 1, sequence, null, null), "{\"work\":true}", "{}"));
        }
        await OutboxAsync(database, store, fixture);
    }
    private static async Task OutboxAsync(DatabaseEngine database, ZoneTreeStore store, RetainedAppendIdentityFixture fixture)
    {
        long bytes = 0;
        foreach (var entry in fixture.ExpectedOutbox)
        {
            var raw = store.Read(view => view.ReadOwnedValue(KeySpace.Partition("outbox", fixture.Stream.Partition, entry.Sequence)))
                ?? throw new InvalidOperationException("A literal outbox entry is missing.");
            bytes += raw.LongLength;
            var actual = OrleansSerialization.Deserialize<OutboxEntry>(raw);
            await RetainedAppendIdentityAssertions.SameAsync(actual, entry);
            await Assert.That(actual.Receipt.CompositionReferences).IsEmpty();
        }
        var count = fixture.ExpectedOutbox.Count;
        var rows = store.Read(view => view.Scan(KeySpace.Partition("outbox", fixture.Stream.Partition), count + 1));
        await Assert.That(rows.HasMore).IsFalse();
        await Assert.That(rows.Records.Length).IsEqualTo(count);
        await RetainedAppendIdentityAssertions.SameAsync(database.GetOutboxStatus("root", fixture.Stream.Partition),
            new OutboxStatus(new(count, 1, count, bytes), []));
    }
}
