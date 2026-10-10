using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueLifecycleBackupRestore
{
    private const string Prefix = "keyload-native-lifecycle-backup-";
    private const string Backup = "backup";
    private const string Target = "target";

    internal static async Task RunAsync(ZoneTreeStore source, QueueLifecycleTestState original, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var root = Path.Combine(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString("N"));
        var backup = Path.Combine(root, Backup);
        var target = Path.Combine(root, Target);
        var failures = new List<Exception>();
        ZoneTreeStore? restored = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            Directory.CreateDirectory(root);
            var image = QueueLifecycleImage.Capture(source, original.Lane);
            var position = source.CreateBackup(backup);
            ZoneTreeStore.Restore(backup, target, UnitExecutionOptions.StorageExecution());
            restored = new(new(target), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
            await Assert.That(restored.Identity.Incarnation).IsNotEqualTo(source.Identity.Incarnation);
            var database = QueueWholeFlowStorage.Open(restored);
            await QueueLifecycleImage.SameAsync(restored, original.Lane, image);
            await QueueLifecycleAccountingAssertions.InitialAsync(restored, original);
            var prior = original.Successes.First(item => item.Operation.Kind == OperationKind.Batch);
            var before = restored.Position;
            await Assert.That(database.Apply(prior.Operation).Error).IsEqualTo(ErrorCode.TokenInvalidated);
            await Assert.That(restored.Position).IsEqualTo(before);
            await QueueLifecycleImage.SameAsync(restored, original.Lane, image);
            await Assert.That(QueueLifecycleOperations.Complete(database, new(original.Partition, original.Time), original.OriginalPending!, DeliveryAction.Ack).Error)
                .IsEqualTo(ErrorCode.TokenInvalidated);
            await QueueLifecycleImage.SameAsync(restored, original.Lane, image);
            var state = new QueueLifecycleTestState(original.Partition, original.Time);
            await QueueLifecycleRestoredContinuation.RunAsync(database, restored, state, token);
            var terminal = QueueLifecycleImage.Capture(restored, original.Lane);
            restored.Dispose();
            restored = new(new(target), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
            var cold = QueueWholeFlowStorage.Open(restored);
            await QueueLifecycleImage.SameAsync(restored, original.Lane, terminal);
            await QueueLifecycleOperations.ReplayAsync(cold, state);
            await QueueLifecycleHealthy.RunAsync(cold, state, token);
            await QueueLifecycleAccountingAssertions.TerminalAsync(restored, state, true);
            await Assert.That(source.Position).IsEqualTo(position);
            await QueueLifecycleImage.SameAsync(source, original.Lane, image);
        }, failures);
        if (restored is not null)
        { ServerFailureObserver.Observe(restored.Dispose, failures); }
        if (failures.Count == QueueLifecycleTestProtocol.None)
        { ServerFailureObserver.Observe(() => Directory.Delete(root, true), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
