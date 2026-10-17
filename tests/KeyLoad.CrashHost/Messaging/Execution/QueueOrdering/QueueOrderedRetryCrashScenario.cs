using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class QueueOrderedRetryCrashScenario
{
    internal static async Task RunAsync(string directory, ZoneTreeStore store, CanonicalCrashBoundary boundary)
    {
        var (database, delivery) = QueueOrderedRetryCrashSeed.Seed(store);
        var command = new DeliveryCommand(QueueOrderedRetryCrashProtocol.CommandId,
            new(QueueOrderedRetryCrashProtocol.Partition, QueueOrderedRetryCrashProtocol.Queue), delivery.Token, DeliveryAction.Nack);
        var operation = database.PrepareQueueRetryOperation(database.NormalizeOperation(
            CrashDatabase.Operation(OperationKind.Delivery, command, command.CommandId)));
        await File.WriteAllBytesAsync(Path.Combine(directory, QueueOrderedRetryCrashProtocol.OperationFile), NativeSerialization.Serialize(operation));
        await File.WriteAllBytesAsync(Path.Combine(directory, QueueOrderedRetryCrashProtocol.DeliveryFile), NativeSerialization.Serialize(delivery));
        boundary.Position = store.Position + QueueOrderedRetryCrashProtocol.One;
        boundary.Armed = true;
        _ = database.Apply(operation).Get<CommitReceipt>();
        await CrashHostPause.WaitForKillAsync();
    }
}
