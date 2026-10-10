using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class QueueLifecycleCrashScenario
{
    internal static async Task RunAsync(string directory, ZoneTreeStore store, CanonicalCrashBoundary boundary)
    {
        var database = QueueLifecycleCrashSeed.Seed(store);
        var id = QueueLifecycleCrashProtocol.CommandId;
        var command = new CommandRequest(id, QueueLifecycleCrashProtocol.Partition,
            [new CancelQueueMessage(QueueLifecycleCrashProtocol.Queue, QueueLifecycleCrashProtocol.Parked,
                QueueLifecycleCrashProtocol.Three, QueueLifecycleCrashProtocol.One),
             new ParkPendingQueueMessage(QueueLifecycleCrashProtocol.Queue, QueueLifecycleCrashProtocol.Pending,
                QueueLifecycleCrashProtocol.Three, QueueLifecycleCrashProtocol.One),
             new RedriveQueueMessage(QueueLifecycleCrashProtocol.Queue, QueueLifecycleCrashProtocol.Pending,
                QueueLifecycleCrashProtocol.Four, QueueLifecycleCrashProtocol.One)]);
        var operation = CrashDatabase.Operation(OperationKind.Batch, command, id);
        await File.WriteAllBytesAsync(Path.Combine(directory, QueueLifecycleCrashProtocol.OperationFile), JsonDefaults.Serialize(operation));
        boundary.Position = store.Position + QueueLifecycleCrashProtocol.One;
        boundary.Armed = true;
        _ = database.Apply(operation).Get<CommitReceipt>();
        await CrashHostPause.WaitForKillAsync();
    }
}
