using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class SubscriptionFilterRefusals
{
    internal static async Task RunAsync(DatabaseEngine db, ZoneTreeStore store,
        ConfigureSubscriptionRequest replacement, CancellationToken token)
    {
        var same = replacement with { CommandId = Guid.NewGuid(), Definition = db.GetSubscription(SubscriptionFilterProtocol.Root, replacement.Subscription).Definition };
        var before = SubscriptionFilterNativeAssertions.Image(store, replacement.Subscription.Source.Partition);
        var unchanged = SubscriptionFilterColdTrial.Apply(db, OperationKind.ConfigureSubscription, same, same.CommandId, token).Get<SubscriptionInfo>();
        await Assert.That(unchanged.Generation).IsEqualTo(SubscriptionFilterProtocol.FirstGeneration);
        await Assert.That(SubscriptionFilterNativeAssertions.Image(store, replacement.Subscription.Source.Partition).SequenceEqual(before)).IsTrue();
        await SubscriptionFilterNativeAssertions.RefusedAsync(db, store, replacement with
        { CommandId = Guid.NewGuid(), ExpectedGeneration = SubscriptionFilterProtocol.NextGeneration }, ErrorCode.RevisionConflict, token);
        await SubscriptionFilterNativeAssertions.RefusedAsync(db, store, replacement with
        { CommandId = Guid.NewGuid(), ExpectedGeneration = 0 }, ErrorCode.Validation, token);
        await SubscriptionFilterNativeAssertions.RefusedAsync(db, store, replacement with
        { CommandId = Guid.NewGuid(), Start = SubscriptionStart.FromNow }, ErrorCode.Validation, token);
        await SubscriptionFilterNativeAssertions.RefusedAsync(db, store, replacement with
        { CommandId = Guid.NewGuid(), Cursor = SubscriptionFilterProtocol.InvalidCursor }, ErrorCode.Validation, token);
        await SubscriptionFilterNativeAssertions.RefusedAsync(db, store, replacement with
        { CommandId = Guid.NewGuid(), Subscription = replacement.Subscription with { GroupId = SubscriptionFilterProtocol.MissingGroup } }, ErrorCode.NotFound, token);
        await SubscriptionFilterNativeAssertions.RefusedAsync(db, store, replacement with
        { CommandId = Guid.NewGuid(), Definition = replacement.Definition with { EventTypes = [SubscriptionFilterProtocol.NewType, SubscriptionFilterProtocol.NewType] } }, ErrorCode.Validation, token);
        await SubscriptionFilterNativeAssertions.RefusedAsync(db, store, replacement with
        { CommandId = Guid.NewGuid(), ExpectedGeneration = null }, ErrorCode.Conflict, token);
    }
}
