using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class SubscriptionFilterProcessAssertions
{
    private const string Root = "root";
    private const string OldWindow = "subscription-window";
    private const string Healthy = "healthy";
    private const string Payload = "{}";
    private const long UpdatedGeneration = 2;
    private const long Checkpoint = 1;
    private const long Tail = 3;

    internal static async Task RecoverAsync(string path, CommitStage stage, CancellationToken token)
    {
        using var store = new ZoneTreeStore(new(path), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        var db = Open(store);
        var group = SubscriptionFilterCrashScenario.Subscription;
        var actual = db.GetSubscription(Root, group);
        await Assert.That(actual.Generation is 1 or UpdatedGeneration).IsTrue();
        await Assert.That(actual.OwnershipEpoch).IsEqualTo(actual.Generation);
        await Assert.That(actual.Checkpoint).IsEqualTo(Checkpoint);
        await Assert.That(actual.Paused).IsTrue();
        var rows = store.Read(view => view.Scan(KeySpace.Partition(OldWindow, group.Source.Partition,
            group.Source.Resource, group.Source.Kind.ToString(), group.Source.StreamId, group.Source.Generation,
            group.GroupId, SubscriptionFilterCrashScenario.OriginalGeneration), SubscriptionFilterCrashScenario.WindowSize));
        await Assert.That(rows.Records.Length).IsEqualTo(actual.Generation == UpdatedGeneration ? 0 : 2);
        var retained = store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(group.Source.Partition, Root, SubscriptionFilterCrashScenario.CommandId)));
        await Assert.That(retained is not null).IsEqualTo(actual.Generation == UpdatedGeneration);
        if (stage >= CommitStage.JournalFlushed)
        { await Assert.That(actual.Generation).IsEqualTo(UpdatedGeneration); }
        var operation = JsonDefaults.Deserialize<ReplicatedOperation>(await File.ReadAllBytesAsync(Path.Combine(path, SubscriptionFilterCrashScenario.OperationFile), token));
        var first = db.ApplyEmbedded(operation, token).Get<SubscriptionInfo>();
        await Assert.That(first.Generation).IsEqualTo(UpdatedGeneration);
        var repeated = db.ApplyEmbedded(operation, token).Get<SubscriptionInfo>();
        await Assert.That(JsonDefaults.Serialize(repeated).AsSpan().SequenceEqual(JsonDefaults.Serialize(first))).IsTrue();
        var old = JsonDefaults.Deserialize<string>(await File.ReadAllBytesAsync(Path.Combine(path, SubscriptionFilterCrashScenario.OldTokenFile), token));
        var ack = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, old, DeliveryAction.Ack);
        await Assert.That(Submit(db, OperationKind.SubscriptionDelivery, ack, ack.CommandId, token).Error).IsEqualTo(ErrorCode.TokenInvalidated);
        var resume = new SetSubscriptionPausedRequest(Guid.NewGuid(), group, UpdatedGeneration, false);
        Submit(db, OperationKind.SetSubscriptionPaused, resume, resume.CommandId, token).Get<SubscriptionInfo>();
        var receive = new ReceiveSubscriptionRequest(Guid.NewGuid(), group, SubscriptionFilterCrashScenario.WindowSize);
        var result = Submit(db, OperationKind.ReceiveSubscription, receive, receive.RequestId, token).Get<ReceiveSubscriptionResult>();
        await Assert.That(result.Deliveries.Single().Event.Position).IsEqualTo(UpdatedGeneration);
        ack = new(Guid.NewGuid(), group, result.Deliveries.Single().Token, DeliveryAction.Ack);
        Submit(db, OperationKind.SubscriptionDelivery, ack, ack.CommandId, token).Get<CommitReceipt>();
        await Assert.That(db.GetSubscription(Root, group).Checkpoint).IsEqualTo(Tail);
        await HealthyAsync(db, group, token);
    }

    private static async Task HealthyAsync(DatabaseEngine db, SubscriptionRef group, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), group.Source.Partition,
            [new PublishTopic(group.Source.Resource, [new(Healthy, SubscriptionFilterCrashScenario.ReplacementType, Payload)])]);
        Submit(db, OperationKind.Batch, command, command.CommandId, token).Get<CommitReceipt>();
        await Assert.That(db.ReadEventSource(Root, new(group.Source), token).Events.Length).IsEqualTo(4);
        var request = new ReceiveSubscriptionRequest(Guid.NewGuid(), group);
        var received = Submit(db, OperationKind.ReceiveSubscription, request, request.RequestId, token).Get<ReceiveSubscriptionResult>();
        await Assert.That(received.Deliveries.Single().Event.Data.EventId).IsEqualTo(Healthy);
        var ack = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, received.Deliveries.Single().Token, DeliveryAction.Ack);
        Submit(db, OperationKind.SubscriptionDelivery, ack, ack.CommandId, token).Get<CommitReceipt>();
        await Assert.That(db.GetSubscription(Root, group).Checkpoint).IsEqualTo(4);
    }

    private static OperationResult Submit<T>(DatabaseEngine db, OperationKind kind, T request, Guid id, CancellationToken token)
        => db.ApplyEmbedded(new(id, kind, Root, default, System.Text.Json.JsonSerializer.Serialize(request, JsonDefaults.Options)), token);

    private static DatabaseEngine Open(ZoneTreeStore store) => new(store, new AuthorizationPolicy(),
        RecoveryExecutionOptions.DatabaseLimits(), RecoveryExecutionOptions.DueWork(), RecoveryExecutionOptions.EventSource(),
        RecoveryExecutionOptions.Messaging(), RecoveryExecutionOptions.GraphExecution(), RecoveryExecutionOptions.ChangeFeedExecution(),
        RecoveryExecutionOptions.BlobExecution(), RecoveryExecutionOptions.NativeClaimsExecution(), RecoveryExecutionOptions.TimeSeriesExecution(),
        RecoveryExecutionOptions.MovementCheckpoints(), UnavailablePartitionMovementCheckpointVerifier.Instance);
}
