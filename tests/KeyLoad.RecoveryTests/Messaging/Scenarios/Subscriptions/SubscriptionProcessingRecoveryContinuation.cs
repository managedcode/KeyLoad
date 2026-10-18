using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class SubscriptionProcessingRecoveryContinuation
{
    private const string Healthy = "healthy-processing";

    internal static byte[] Key(string space, SubscriptionRef group, params object?[] suffix)
        => KeySpace.Partition(space, group.Source.Partition, new object?[]
        { group.Source.Resource, group.Source.Kind.ToString(), group.Source.StreamId, group.Source.Generation, group.GroupId }
            .Concat(suffix).ToArray());

    internal static async Task RunAsync(DatabaseEngine db, ZoneTreeStore store, string path,
        Command<DatabaseCommand> original, SubscriptionProcessingResult first, CancellationToken token)
    {
        var group = SubscriptionCrashScenario.Subscription;
        await ReplayAsync(db, store, original, first, token);
        var request = NativeSerialization.Deserialize<SubscriptionProcessingRequest>(original.Value!.Input.Span, NativeValidationProfile.PublicInputElements);
        var retry = request with
        {
            CommandId = Guid.NewGuid()
        };
        var reused = Submit(db, OperationKind.SubscriptionProcessing, retry, retry.CommandId)
            .Get<SubscriptionProcessingResult>();
        await Assert.That(reused.AlreadyProcessed).IsTrue();
        await Assert.That(reused.OriginalEffectsToken).IsEqualTo(first.OriginalEffectsToken);
        await Assert.That(NativeSerialization.Serialize(reused.Receipt).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(first.Receipt))).IsTrue();
        await HealthyAsync(db, token);
        await ReplayAsync(db, store, original, first, token);
        var position = store.Position;
        var identity = NativeSerialization.Serialize(store.Identity);
        var outcomeKey = KeySpace.PartitionOutcome(group.Source.Partition, CrashFixtureValues.Principal, original.CommandId);
        var inboxKey = Key("subscription-inbox", group, 1L, CrashFixtureValues.Handler, 1L);
        var outcome = store.Read(view => view.ReadOwnedValue(outcomeKey))!;
        var inbox = store.Read(view => view.ReadOwnedValue(inboxKey))!;
        var source = db.ReadEventSource(CrashFixtureValues.Principal, new(group.Source), token).Events;
        store.Dispose();
        using var cold = new ZoneTreeStore(new(path), RecoveryExecutionOptions.StorageExecution(),
            RecoveryExecutionOptions.PointCacheExecution());
        var reopened = TargetInboxRecoveryDatabase.Open(cold);
        await Assert.That(cold.Position).IsEqualTo(position);
        await Assert.That(NativeSerialization.Serialize(cold.Identity).AsSpan().SequenceEqual(identity)).IsTrue();
        await Assert.That(cold.Read(view => view.ReadOwnedValue(outcomeKey))!.AsSpan().SequenceEqual(outcome)).IsTrue();
        await Assert.That(cold.Read(view => view.ReadOwnedValue(inboxKey))!.AsSpan().SequenceEqual(inbox)).IsTrue();
        await Assert.That(NativeSerialization.Serialize(reopened.ReadEventSource(CrashFixtureValues.Principal,
            new(group.Source), token).Events).AsSpan().SequenceEqual(NativeSerialization.Serialize(source))).IsTrue();
        await ReplayAsync(reopened, cold, original, first, token);
        await SourceAsync(reopened, true, token);
        await Assert.That(reopened.GetSubscription(CrashFixtureValues.Principal, SubscriptionCrashScenario.Subscription).Checkpoint).IsEqualTo(2L);
    }

    private static async Task ReplayAsync(DatabaseEngine db, ZoneTreeStore store,
        Command<DatabaseCommand> original, SubscriptionProcessingResult expected, CancellationToken token)
    {
        var key = KeySpace.PartitionOutcome(SubscriptionCrashScenario.Partition, CrashFixtureValues.Principal, original.CommandId);
        var saved = store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException("The original native processing receipt is absent.");
        var inboxKey = Key("subscription-inbox", SubscriptionCrashScenario.Subscription, 1L, CrashFixtureValues.Handler, 1L);
        var inbox = store.Read(view => view.ReadOwnedValue(inboxKey))
            ?? throw new InvalidOperationException("The original native processing inbox is absent.");
        var position = store.Position;
        for (var repeat = 0; repeat < 2; repeat++)
        {
            var result = db.Apply(original).Get<SubscriptionProcessingResult>();
            await Assert.That(NativeSerialization.Serialize(result).AsSpan().SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
            await Assert.That(store.Position).IsEqualTo(position);
            await Assert.That(store.Read(view => view.ReadOwnedValue(key))!.AsSpan().SequenceEqual(saved)).IsTrue();
            await Assert.That(store.Read(view => view.ReadOwnedValue(inboxKey))!.AsSpan().SequenceEqual(inbox)).IsTrue();
            await SubscriptionProcessingRecoveryAssertions.DocumentAsync(db, true, token);
        }
    }

    private static async Task HealthyAsync(DatabaseEngine db, CancellationToken token)
    {
        var group = SubscriptionCrashScenario.Subscription;
        var publish = new CommandRequest(Guid.NewGuid(), group.Source.Partition,
            [new PublishTopic(group.Source.Resource, [new(Healthy, CrashFixtureValues.EventType, CrashFixtureValues.EmptyJson)])]);
        Submit(db, OperationKind.Batch, publish, publish.CommandId).Get<CommitReceipt>();
        var receive = new ReceiveSubscriptionRequest(Guid.NewGuid(), group);
        var delivered = Submit(db, OperationKind.ReceiveSubscription, receive, receive.RequestId).Get<ReceiveSubscriptionResult>().Deliveries.Single();
        await Assert.That(delivered.Event.Position).IsEqualTo(2L);
        await Assert.That(delivered.Event.Data).IsEqualTo(new EventData(Healthy, CrashFixtureValues.EventType, CrashFixtureValues.EmptyJson));
        await Assert.That(delivered.Attempt).IsEqualTo(1);
        await Assert.That(delivered.LeaseVersion).IsEqualTo(1L);
        var ack = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, delivered.Token, DeliveryAction.Ack);
        var receipt = Submit(db, OperationKind.SubscriptionDelivery, ack, ack.CommandId).Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(ack.CommandId);
        await Assert.That(receipt.Mutations.Single()).IsEqualTo(new MutationReceipt("Ack", CrashFixtureValues.Topic, "2", 1L));
        await Assert.That(db.GetSubscription(CrashFixtureValues.Principal, group).Checkpoint).IsEqualTo(2L);
        await SourceAsync(db, true, token);
    }

    internal static async Task SourceAsync(DatabaseEngine db, bool healthy, CancellationToken token)
    {
        var group = SubscriptionCrashScenario.Subscription;
        var page = db.ReadEventSource(CrashFixtureValues.Principal, new(group.Source), token);
        await Assert.That(page.Events.Length).IsEqualTo(healthy ? 2 : 1);
        await Assert.That(page.Head.TailPosition).IsEqualTo(healthy ? 2L : 1L);
        await Assert.That(page.Head.FirstAvailablePosition).IsEqualTo(1L);
        await Assert.That(page.Head.Generation).IsEqualTo(1L);
        for (var index = 0; index < page.Events.Length; index++)
        {
            await Assert.That(page.Events[index].Position).IsEqualTo(index + 1L);
            await Assert.That(page.Events[index].EventSequence).IsEqualTo(index + 1L);
            await Assert.That(page.Events[index].Source).IsEqualTo(group.Source);
            await Assert.That(page.Events[index].Data).IsEqualTo(new EventData(index == 0 ? CrashFixtureValues.Input : Healthy,
                CrashFixtureValues.EventType, CrashFixtureValues.EmptyJson));
        }
    }

    private static Result<object> Submit<T>(DatabaseEngine db, OperationKind kind, T request, Guid id)
        => db.Apply(db.CreateCommand(kind, id, CrashFixtureValues.Principal, TimeProvider.System.GetUtcNow(), JsonSerializer.Serialize(request, JsonDefaults.Options)));
}
