using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Messaging;

internal static class SubscriptionFilterWindowFaults
{
    private const long ForeignPosition = 99;
    private const long ExcessPosition = 100;

    internal static async Task RunAsync(DatabaseEngine db, ZoneTreeStore store,
        ConfigureSubscriptionRequest replacement, CancellationToken token)
    {
        var group = replacement.Subscription;
        var prefix = KeySpace.Partition(SubscriptionFilterProtocol.Window, group.Source.Partition,
            group.Source.Resource, group.Source.Kind.ToString(), group.Source.StreamId,
            group.Source.Generation, group.GroupId, SubscriptionFilterProtocol.FirstGeneration);
        var original = SubscriptionFilterAssertions.Image(store, group.Source.Partition);
        var row = store.Read(view => view.Scan(prefix, SubscriptionFilterProtocol.WindowSize).Records.First());
        var wrong = KeySpace.Partition(SubscriptionFilterProtocol.Window, group.Source.Partition,
            group.Source.Resource, group.Source.Kind.ToString(), group.Source.StreamId,
            group.Source.Generation, group.GroupId, SubscriptionFilterProtocol.FirstGeneration, ForeignPosition);
        var excess = KeySpace.Partition(SubscriptionFilterProtocol.Window, group.Source.Partition,
            group.Source.Resource, group.Source.Kind.ToString(), group.Source.StreamId,
            group.Source.Generation, group.GroupId, SubscriptionFilterProtocol.FirstGeneration, ExcessPosition);
        store.Commit((tx, _) => { tx.Put(wrong, row.Value.ToArray()); return true; });
        await SubscriptionFilterAssertions.RefusedAsync(db, store, replacement with
        {
            CommandId = Guid.NewGuid()
        }, ErrorCode.Corruption, token);
        store.Commit((tx, _) => { tx.Put(excess, row.Value.ToArray()); return true; });
        await SubscriptionFilterAssertions.RefusedAsync(db, store, replacement with
        {
            CommandId = Guid.NewGuid()
        }, ErrorCode.Corruption, token);
        store.Commit((tx, _) => { tx.Delete(wrong); tx.Delete(excess); return true; });
        await Assert.That(SubscriptionFilterAssertions.Image(store, group.Source.Partition)).IsEquivalentTo(original, CollectionOrdering.Matching);
    }
}
