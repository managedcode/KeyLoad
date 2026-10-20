namespace KeyLoad.UnitTests.Features.Messaging;

internal static class SubscriptionOldCutOutbox
{
    internal static async Task RequireAsync(DatabaseEngine database, SubscriptionOldCutState state)
    {
        var partition = state.Group.Source.Partition;
        var epoch = SubscriptionOldCutState.Epoch;
        OutboxEntry[] expected = [
            new(1, 0, state.Producer.Token, epoch, new PublishTopic(SubscriptionOldCutState.Topic,
                [new("event1", "Created", "{\"n\":1}"), new("event2", "Created", "{\"n\":2}"), new("event3", "Created", "{\"n\":3}")]),
                new("publishTopic", SubscriptionOldCutState.Topic, "event3", 3)),
            Document(2, "input2", state.OriginalProcessing.Receipt.Token, partition, epoch),
            new(3, 0, state.FreshProducer.Token, epoch.AddSeconds(31), new PublishTopic(SubscriptionOldCutState.Topic,
                [new("event4", "Created", "{\"n\":4}")]), new("publishTopic", SubscriptionOldCutState.Topic, "event4", 4)),
            Document(4, "input4", state.FreshProcessing.Token, partition, epoch.AddSeconds(31))];
        await EntriesAsync(database, partition, expected);
    }
    private static OutboxEntry Document(long sequence, string id, CommitToken commit, PartitionRef partition,
        DateTimeOffset time) => new(sequence, 0, commit, time,
            new PutDocument(SubscriptionOldCutState.Collection, id, SubscriptionOldCutState.EffectJson, 0),
            new("putDocument", SubscriptionOldCutState.Collection, id, 1), After: new(
                new(partition, SubscriptionOldCutState.Collection, id), 1, SubscriptionOldCutState.EffectJson, new(), time));

    internal static async Task EntriesAsync(DatabaseEngine database, PartitionRef partition,
        IReadOnlyList<OutboxEntry> expected)
    {
        long bytes = 0;
        foreach (var entry in expected)
        {
            var raw = database.Store.Read(view => view.ReadOwnedValue(KeySpace.Partition("outbox", partition, entry.Sequence)))
                ?? throw new InvalidOperationException("A committed execution outbox entry is missing.");
            bytes += raw.LongLength;
            var actual = OrleansSerialization.Deserialize<OutboxEntry>(raw);
            await SubscriptionOldCutAssertions.SameAsync(actual, entry);
            await Assert.That(actual.Receipt.CompositionReferences).IsEmpty();
        }
        var page = database.Store.Read(view => view.Scan(KeySpace.Partition("outbox", partition), expected.Count + 1));
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.Records.Length).IsEqualTo(expected.Count);
        await SubscriptionOldCutAssertions.SameAsync(database.GetOutboxStatus("root", partition),
            new OutboxStatus(new(expected.Count, 1, expected.Count, bytes), []));
    }
}
