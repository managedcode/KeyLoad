using KeyLoad.FunctionalTests.BackupRestore;
using KeyLoad.FunctionalTests.ResourceExecution;
using KeyLoad.FunctionalTests.StorageRecovery.Assertions;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.Messaging;

internal static class SubscriptionOldCutFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var clock = new ControlledReadClock(SubscriptionOldCutState.Epoch);
        TestDatabase? fixture = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            fixture = new TestDatabase(timeProvider: clock);
            await OwnedAsync(fixture, token);
        }, failures);
        if (fixture is not null)
        {
            ServerFailureObserver.Observe(fixture.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task OwnedAsync(TestDatabase fixture, CancellationToken token)
    {
        var state = new SubscriptionOldCutState(fixture.Partition);
        await SubscriptionOldCutSeed.RunAsync(fixture, state);
        var root = Path.Combine(fixture.Directory, "old-cut-backup");
        var target = Path.Combine(fixture.Directory, "restored-owner");
        await Assert.That(fixture.Database.LastApplied).IsGreaterThan(MissingAppliedIndex);
        var capture = MixedEventingBackupRestore.Capture(fixture, root, token);
        var backupPosition = capture.Position;
        await SubscriptionOldCutAssertions.StatusAsync(fixture.Database, state, 1, 0, 3, 3);
        var first = new SubscriptionDeliveryCommand(Guid.NewGuid(), state.Group,
            state.OriginalClaim.Deliveries[0].Token, DeliveryAction.Ack);
        state.Apply(fixture.Database, OperationKind.SubscriptionDelivery, first, first.CommandId).Get<CommitReceipt>();
        await SubscriptionOldCutAssertions.StatusAsync(fixture.Database, state, 1, 3, 3, 3);
        await Assert.That(fixture.Store.Position > backupPosition).IsTrue();
        var original = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var identity = ReadCutStoreStateAssertions.SnapshotIdentity(fixture.Store);
        var archive = Directory.GetFiles(root).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes,
            StringComparer.Ordinal);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => RestoreAsync(fixture, state, capture, root, target, token), failures);
        await ServerFailureObserver.ObserveAsync(() => SubscriptionOldCutAssertions.ConservedAsync(fixture.Store,
            original, position, identity), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(Directory.GetFiles(root).Select(path => Path.GetFileName(path)!).Order(StringComparer.Ordinal))
                .IsEquivalentTo(archive.Keys.Order(StringComparer.Ordinal), TUnit.Assertions.Enums.CollectionOrdering.Matching);
            foreach (var item in archive)
            {
                var current = await File.ReadAllBytesAsync(Path.Combine(root, item.Key), token);
                await Assert.That(current.AsSpan().SequenceEqual(item.Value)).IsTrue();
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task RestoreAsync(TestDatabase fixture, SubscriptionOldCutState state,
        (StoreIdentity Identity, long Position) capture, string backup, string target, CancellationToken token)
    {
        var restoredPosition = await MixedEventingBackupRestore.RestoreAsync(fixture, capture, backup, target, token);
        ZoneTreeStore? store = null;
        var failures = new List<Exception>();
        string[] bytes = [];
        StoreIdentity identity = null!;
        long position = 0;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            store = new(new(target), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
            await Assert.That(store.Identity.NodeId).IsNotEqualTo(fixture.Store.Identity.NodeId);
            await Assert.That(store.Identity.Incarnation).IsNotEqualTo(fixture.Store.Identity.Incarnation);
            await Assert.That(store.Identity.SigningKey.Span.SequenceEqual(fixture.Store.Identity.SigningKey.Span)).IsFalse();
            await Assert.That(store.Identity.DispatchPaused).IsTrue();
            await Assert.That(store.Position).IsEqualTo(restoredPosition);
            var database = SubscriptionOldCutCold.Open(store, state);
            var placement = database.ReadAtomicPartitionPlacement("root", state.Group.Source.Partition);
            await Assert.That(placement.Incarnation).IsEqualTo(store.Identity.Incarnation);
            await SubscriptionOldCutAssertions.StatusAsync(database, state, 1, 0, 3, 3);
            await SubscriptionOldCutRefusal.RunAsync(database, store, state.OriginalProcessingOperation,
                ErrorCode.TokenInvalidated, "This command belongs to a previous database incarnation.");
            state.Successes.Clear();
            await SubscriptionOldCutReset.RunAsync(database, store, state);
            await SubscriptionOldCutHealthy.RunAsync(database, store, state);
            bytes = QueueWholeFlowStorage.Bytes(store);
            position = store.Position;
            identity = ReadCutStoreStateAssertions.SnapshotIdentity(store);
        }, failures);
        if (store is not null)
        {
            ServerFailureObserver.Observe(() =>
            {
                bytes = QueueWholeFlowStorage.Bytes(store);
                position = store.Position;
                identity = ReadCutStoreStateAssertions.SnapshotIdentity(store);
            }, failures);
            ServerFailureObserver.Observe(store.Dispose, failures);
        }
        if (bytes.Length > 0)
        {
            await ServerFailureObserver.ObserveAsync(() => SubscriptionOldCutCold.RunAsync(target, state, bytes,
                position, identity, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private const long MissingAppliedIndex = 0;
}
