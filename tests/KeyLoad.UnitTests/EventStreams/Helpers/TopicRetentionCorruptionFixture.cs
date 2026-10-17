using KeyLoad.Core;
namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class TopicRetentionCorruptionFixture
{
    internal static byte[] GroupKey(TopicRetentionFixture fixture) => KeySpace.Partition("subscription", fixture.Owner.Partition,
        "topic", "Topic", null, 1L, "first");
    internal static byte[] DigestKey(TopicRetentionFixture fixture, string id) => KeySpace.Partition("topic-event-id", fixture.Owner.Partition,
        "topic", 1L, id, "retained-digest");
    internal static void Write(TopicRetentionFixture fixture, byte[] key, byte[] value)
        => fixture.Store.Commit((transaction, _) => { transaction.Put(key, value); return true; });
    internal static void Delete(TopicRetentionFixture fixture, byte[] key)
        => fixture.Store.Commit((transaction, _) => { transaction.Delete(key); return true; });
    internal static async Task HealthyAsync(TopicRetentionFixture fixture)
    {
        var command = new CommandRequest(Guid.NewGuid(), fixture.Owner.Partition, [new PublishTopic("topic", [new("event4", "Created", "{\"n\":4}")])]);
        var receipt = fixture.Submit(command).Get<CommitReceipt>();
        await Assert.That(Convert.ToHexString(NativeSerialization.Serialize(receipt.Mutations.Single())))
            .IsEqualTo(Convert.ToHexString(NativeSerialization.Serialize(new MutationReceipt("publishTopic", "topic", "event4", 4))));
        var page = fixture.Database.ReadEventSource("root", new(fixture.Source, AfterPosition: 3));
        await Assert.That(page.Events.Single().Data).IsEqualTo(new EventData("event4", "Created", "{\"n\":4}"));
        await Assert.That(page.Events.Single().EventSequence).IsEqualTo(4L);
        await Assert.That(page.Head).IsEqualTo(new EventSourceHead(4, 3, 1));
    }
}
