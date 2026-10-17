
namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class TopicRetentionIdentityFlow
{
    internal static async Task VerifyAsync(TopicRetentionFixture fixture)
    {
        var position = fixture.Store.Position;
        var original = fixture.Submit(fixture.Publication).Get<CommitReceipt>();
        await Assert.That(JsonDefaults.Serialize(original).AsSpan().SequenceEqual(JsonDefaults.Serialize(fixture.PublicationReceipt))).IsTrue();
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
        var same = new CommandRequest(Guid.NewGuid(), fixture.Owner.Partition,
            [new PublishTopic("topic", [new("event1", "Created", TopicRetentionFixture.Payload)])]);
        await TopicRetentionPostPurgeRejection.RejectAsync(fixture, same, ErrorCode.DuplicateEventId);
        var conflict = new CommandRequest(Guid.NewGuid(), fixture.Owner.Partition,
            [new PublishTopic("topic", [new("event1", "Created", "{\"n\":99}")])]);
        await TopicRetentionPostPurgeRejection.RejectAsync(fixture, conflict, ErrorCode.Conflict);
        var healthy = new CommandRequest(Guid.NewGuid(), fixture.Owner.Partition,
            [new PublishTopic("topic", [new("event4", "Created", "{\"n\":4}")])]);
        fixture.Submit(healthy).Get<CommitReceipt>();
        var record = fixture.Database.ReadEventSource("root", new(fixture.Source, AfterPosition: 3)).Events.Single();
        await Assert.That(record.Position).IsEqualTo(4L);
        await Assert.That(record.EventSequence).IsEqualTo(4L);
        await Assert.That(record.Data).IsEqualTo(new EventData("event4", "Created", "{\"n\":4}"));
    }
}
