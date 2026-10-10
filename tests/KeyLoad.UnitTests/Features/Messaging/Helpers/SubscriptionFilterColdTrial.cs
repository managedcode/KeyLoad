using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using TUnit.Assertions.Enums;
using static KeyLoad.UnitTests.Features.Messaging.SubscriptionTestActions;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class SubscriptionFilterColdTrial
{
    internal static async Task RunAsync(CancellationToken token)
    {
        using var fixture = new TestDatabase();
        fixture.Configure(SubscriptionFilterProtocol.Topic, ResourceKind.Topic);
        fixture.Commit(new PublishTopic(SubscriptionFilterProtocol.Topic,
            [new(SubscriptionFilterProtocol.FirstEvent, SubscriptionFilterProtocol.OldType, SubscriptionFilterProtocol.Payload),
             new(SubscriptionFilterProtocol.SecondEvent, SubscriptionFilterProtocol.NewType, SubscriptionFilterProtocol.Payload),
             new(SubscriptionFilterProtocol.ThirdEvent, SubscriptionFilterProtocol.OldType, SubscriptionFilterProtocol.Payload)]));
        var group = Configure(fixture, SubscriptionFilterProtocol.Group, new() { MaxWindow = SubscriptionFilterProtocol.WindowSize });
        var independent = Configure(fixture, SubscriptionFilterProtocol.Independent);
        var received = Receive(fixture, group);
        Complete(fixture, group, received.Deliveries[0]);
        Complete(fixture, group, received.Deliveries[2]);
        var replacement = new ConfigureSubscriptionRequest(Guid.NewGuid(), group,
            new(SubscriptionFilterProtocol.Root) { EventTypes = [SubscriptionFilterProtocol.NewType] },
            ExpectedGeneration: SubscriptionFilterProtocol.FirstGeneration);
        await SubscriptionFilterNativeAssertions.RefusedAsync(fixture.Database, fixture.Store, replacement, ErrorCode.Conflict, token);
        var pause = new SetSubscriptionPausedRequest(Guid.NewGuid(), group, SubscriptionFilterProtocol.FirstGeneration, true);
        Apply(fixture.Database, OperationKind.SetSubscriptionPaused, pause, pause.CommandId, token).Get<SubscriptionInfo>();
        var cut = SubscriptionFilterNativeAssertions.Image(fixture.Store, fixture.Partition);
        fixture.Store.Dispose();
        using var first = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var db = QueueWholeFlowStorage.Open(first);
        await Assert.That(SubscriptionFilterNativeAssertions.Image(first, fixture.Partition)).IsEquivalentTo(cut, CollectionOrdering.Matching);
        await SubscriptionFilterNativeAssertions.StatusAsync(db, group, SubscriptionFilterProtocol.FirstGeneration, SubscriptionFilterProtocol.GapCheckpoint, true);
        await SubscriptionFilterRefusals.RunAsync(db, first, replacement, token);
        await SubscriptionFilterWindowFaults.RunAsync(db, first, replacement, token);
        replacement = replacement with { CommandId = Guid.NewGuid() };
        var original = Apply(db, OperationKind.ConfigureSubscription, replacement, replacement.CommandId, token);
        var updated = original.Get<SubscriptionInfo>();
        await Assert.That(updated.IssuedPosition).IsEqualTo(SubscriptionFilterProtocol.GapCheckpoint);
        await SubscriptionFilterNativeAssertions.StatusAsync(db, group, SubscriptionFilterProtocol.NextGeneration, SubscriptionFilterProtocol.GapCheckpoint, true);
        await SubscriptionFilterContinuation.RunAsync(db, group, independent, received, token);
        var image = SubscriptionFilterNativeAssertions.Image(first, fixture.Partition);
        first.Dispose();
        using var second = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var cold = QueueWholeFlowStorage.Open(second);
        await NativeReplayResultAssertions.Same<SubscriptionInfo>(Apply(cold, OperationKind.ConfigureSubscription, replacement, replacement.CommandId, token), original);
        await Assert.That(SubscriptionFilterNativeAssertions.Image(second, fixture.Partition)).IsEquivalentTo(image, CollectionOrdering.Matching);
        await SubscriptionFilterNativeAssertions.StatusAsync(cold, group, SubscriptionFilterProtocol.NextGeneration, SubscriptionFilterProtocol.WindowSize, false);
        await SubscriptionFilterContinuation.HealthyAsync(cold, group, token);
        await SubscriptionFilterTailFlow.RunAsync(cold, group.Source, token);
    }

    internal static OperationResult Apply<T>(DatabaseEngine db, OperationKind kind, T payload, Guid id, CancellationToken token)
        => db.ApplyEmbedded(new(id, kind, SubscriptionFilterProtocol.Root, default,
            JsonSerializer.Serialize(payload, JsonDefaults.Options)), token);
}
