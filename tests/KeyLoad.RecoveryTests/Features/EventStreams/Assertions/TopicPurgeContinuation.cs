using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.CrashHost;
namespace KeyLoad.RecoveryTests.Features.EventStreams;

internal static class TopicPurgeContinuation
{
    internal static async Task VerifyAsync(DatabaseEngine database, ReplicatedOperation original, bool pinned)
    {
        foreach (var changed in new[] { false, true })
        {
            var id = Guid.NewGuid();
            var request = new CommandRequest(id, TopicPurgeCrashContract.Partition, [new PublishTopic(TopicPurgeCrashContract.Topic,
                [changed ? new("event1", "Created", "{\"n\":99}") : TopicPurgeCrashContract.Event(1)])]);
            var operation = original with { Id = id, PayloadJson = JsonSerializer.Serialize(request, JsonDefaults.Options) };
            var position = database.Store.Position;
            var protectedBytes = TopicPurgeCrashScenario.ProtectedBytes(database.Store);
            var outcome = database.Apply(operation);
            await Assert.That(outcome.Error).IsEqualTo(changed ? ErrorCode.Conflict : ErrorCode.DuplicateEventId);
            await Assert.That(database.Store.Position).IsEqualTo(position + 1);
            await TopicPurgeReceiptAssertions.StableReplayAsync(database, operation, outcome);
            await Assert.That(TopicPurgeCrashScenario.ProtectedBytes(database.Store)).IsEquivalentTo(protectedBytes);
        }
        var healthy = new CommandRequest(TopicPurgeCrashContract.HealthyId, TopicPurgeCrashContract.Partition,
            [new PublishTopic(TopicPurgeCrashContract.Topic, [TopicPurgeCrashContract.Event(4)])]);
        var healthyOperation = original with { Id = healthy.CommandId, PayloadJson = JsonSerializer.Serialize(healthy, JsonDefaults.Options) };
        var receipt = database.Apply(healthyOperation).Get<CommitReceipt>();
        await Assert.That(receipt.Mutations.Single()).IsEqualTo(new MutationReceipt("publishTopic", TopicPurgeCrashContract.Topic, "event4", 4));
        var page = database.ReadEventSource("root", new(TopicPurgeCrashContract.Source, AfterPosition: 3));
        await Assert.That(page.Events.Single()).IsEqualTo(new SourceEventRecord(TopicPurgeCrashContract.Source, 4, 4, TopicPurgeCrashContract.Event(4), original.EvaluatedAt));
        var claimId = Guid.NewGuid();
        var claim = new ReceiveRequest(claimId, TopicPurgeCrashContract.Lane);
        var claimed = database.Apply(original with
        {
            Id = claimId,
            Kind = OperationKind.Receive,
            PayloadJson = JsonSerializer.Serialize(claim, JsonDefaults.Options)
        }).Get<ReceiveResult>();
        var delivery = claimed.Deliveries.Single();
        await Assert.That(delivery.Id).IsEqualTo("retained");
        await Assert.That(delivery.PayloadJson).IsEqualTo(TopicPurgeCrashContract.QueueJson);
        await Assert.That(delivery.HeadersJson).IsEqualTo(TopicPurgeCrashContract.HeadersJson);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(1L);
        await Assert.That(delivery.DeliveryGeneration).IsEqualTo(1L);
        await Assert.That(delivery.Attempt).IsEqualTo(1);
        await Assert.That(delivery.LeaseUntil).IsEqualTo(original.EvaluatedAt.AddSeconds(30));
        await Assert.That(string.IsNullOrWhiteSpace(delivery.Token)).IsFalse();
        var inspection = database.InspectMessage("root", TopicPurgeCrashContract.Lane, "retained")!;
        await Assert.That(inspection).IsEqualTo(new MessageInspection(
            new("retained", MessageState.Leased, 1, 2, 1, null, null, "root", 1, original.EvaluatedAt.AddSeconds(30), 1),
            TopicPurgeCrashContract.QueueJson, TopicPurgeCrashContract.HeadersJson));
        await FinalAsync(database, pinned);
    }
    internal static async Task FinalAsync(DatabaseEngine database, bool pinned)
    {
        var page = database.ReadEventSource("root", new(TopicPurgeCrashContract.Source, AfterPosition: 3));
        await Assert.That(page.Events.Single().Data).IsEqualTo(TopicPurgeCrashContract.Event(4));
        await Assert.That(page.Events.Single().EventSequence).IsEqualTo(4L);
        await Assert.That(page.Events.Single().Position).IsEqualTo(4L);
        await Assert.That(page.Head).IsEqualTo(new EventSourceHead(4, pinned ? 2 : 3, 1));
        var message = database.InspectMessage("root", TopicPurgeCrashContract.Lane, "retained")!;
        await Assert.That(message.Metadata.State).IsEqualTo(MessageState.Leased);
        await Assert.That(message.Metadata.Attempts).IsEqualTo(1);
        await Assert.That(message.PayloadJson).IsEqualTo(TopicPurgeCrashContract.QueueJson);
        await Assert.That(message.HeadersJson).IsEqualTo(TopicPurgeCrashContract.HeadersJson);
        await Assert.That(database.GetSubscription("root", TopicPurgeCrashContract.Subscription).Paused).IsTrue();
        await Assert.That(database.GetDocument("root", new(TopicPurgeCrashContract.Partition, TopicPurgeCrashContract.Documents, "retained"))!.Json).IsEqualTo(TopicPurgeCrashContract.DocumentJson);
    }
}
