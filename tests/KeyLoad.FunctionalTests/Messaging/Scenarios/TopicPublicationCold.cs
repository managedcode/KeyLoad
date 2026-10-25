using KeyLoad.FunctionalTests.StorageRecovery.Assertions;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.Messaging;

internal static class TopicPublicationCold
{
    internal static async Task VerifyAsync(TestDatabase fixture, Command<DatabaseCommand> command,
        CommitReceipt receipt, ReadEventSourceRequest request, EventSourcePage expected)
    {
        var identity = fixture.Store.Identity;
        var position = fixture.Store.Position;
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var originalCursor = fixture.Database.Verify<SourceCursor>(expected.Cursor);
        var lifetime = UnitExecutionOptions.EventSource().Value.CursorLifetime;
        fixture.JoinOwnedResources();
        for (var cycle = 0; cycle < 2; cycle++)
        {
            using var store = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution,
                UnitExecutionOptions.PointCacheExecution());
            var owner = fixture.CreateConfiguredDatabase(store);
            await ReadCutStoreStateAssertions.AssertIdentityAndPositionAsync(store, identity, position);
            var earliest = owner.EvaluationClock.GetUtcNow() + lifetime;
            var page = owner.ReadEventSource("root", request);
            var latest = owner.EvaluationClock.GetUtcNow() + lifetime;
            var cursor = owner.Verify<SourceCursor>(page.Cursor);
            await Assert.That(cursor).IsEqualTo(originalCursor with { ExpiresAt = cursor.ExpiresAt });
            await Assert.That(cursor.ExpiresAt).IsGreaterThanOrEqualTo(earliest);
            await Assert.That(cursor.ExpiresAt).IsLessThanOrEqualTo(latest);
            await Assert.That(JsonDefaults.Serialize(page).AsSpan()
                .SequenceEqual(JsonDefaults.Serialize(expected with { Cursor = page.Cursor }))).IsTrue();
            var replay = owner.Apply(command).Get<CommitReceipt>();
            await Assert.That(JsonDefaults.Serialize(replay).AsSpan().SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();
            await QueueStoredCapacityAssertions.ImageAsync(store, position, image);
        }
    }
}
