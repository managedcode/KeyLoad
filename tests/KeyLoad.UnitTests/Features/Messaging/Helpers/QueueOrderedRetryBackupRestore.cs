using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueOrderedRetryBackupRestore
{
    private const string Prefix = "keyload-ordered-retry-backup-";
    private const string Backup = "backup";
    private const string Target = "target";

    internal static async Task RunAsync(ZoneTreeStore source, QueueOrderedRetryState original, CancellationToken token)
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
            var image = QueueOrderedRetryImage.Capture(source, original.Lane);
            var position = source.CreateBackup(backup);
            ZoneTreeStore.Restore(backup, target, UnitExecutionOptions.StorageExecution());
            restored = new(new(target), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
            await Assert.That(restored.Identity.Incarnation).IsNotEqualTo(source.Identity.Incarnation);
            var database = QueueWholeFlowStorage.Open(restored);
            await QueueOrderedRetryImage.SameAsync(restored, original.Lane, image);
            await QueueOrderedRetryRestoreRefusal.RunAsync(database, original);
            var state = new QueueOrderedRetryState(original.Partition, original.Time, original.ParkedHead, original.Principal);
            await QueueOrderedRetryRestoreAuthority.RepairAsync(database, state, original, token);
            await QueueOrderedRetryRestoredContinuation.RunAsync(database, state, token);
            await QueueOrderedRetryRestoredAssertions.TerminalAsync(database, state, healthy: false);
            var terminal = QueueOrderedRetryImage.Capture(restored, state.Lane);
            restored.Dispose();
            restored = new(new(target), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
            var cold = QueueWholeFlowStorage.Open(restored);
            await QueueOrderedRetryImage.SameAsync(restored, state.Lane, terminal);
            await QueueOrderedRetryRestoreAuthority.RequireAsync(cold, state);
            await QueueOrderedRetryOperations.ReplayAsync(cold, state);
            await QueueOrderedRetryHealthy.RunAsync(cold, state, token);
            await QueueOrderedRetryRestoredAssertions.TerminalAsync(cold, state, healthy: true);
            await QueueOrderedRetryRestoreAuthority.RequireAsync(cold, state);
            await Assert.That(source.Position).IsEqualTo(position);
            await QueueOrderedRetryImage.SameAsync(source, original.Lane, image);
        }, failures);
        if (restored is not null)
        { ServerFailureObserver.Observe(restored.Dispose, failures); }
        if (failures.Count == QueueOrderedRetryProtocol.Initial)
        { ServerFailureObserver.Observe(() => Directory.Delete(root, true), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
