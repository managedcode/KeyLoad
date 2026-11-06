using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.ProcessRecoveryTests;

internal static class QueueOrderedRetryCrashScenario
{
    internal static async Task RunAsync(string directory, ZoneTreeStore store, CanonicalCrashBoundary boundary)
    {
        var (database, delivery) = QueueOrderedRetryCrashSeed.Seed(store);
        var command = new DeliveryCommand(QueueOrderedRetryCrashProtocol.CommandId,
            new(QueueOrderedRetryCrashProtocol.Partition, QueueOrderedRetryCrashProtocol.Queue), delivery.Token, DeliveryAction.Nack);
        var operation = database.PrepareQueueRetryOperation(database.OwnCommand(
            CrashDatabase.Operation(database, OperationKind.Delivery, command, command.CommandId)));
        await File.WriteAllBytesAsync(Path.Combine(directory, QueueOrderedRetryCrashProtocol.OperationFile), OrleansSerialization.Serialize(operation));
        await File.WriteAllBytesAsync(Path.Combine(directory, QueueOrderedRetryCrashProtocol.DeliveryFile), OrleansSerialization.Serialize(delivery));
        await boundary.ArmAsync(store);
        _ = database.Apply(operation).Get<CommitReceipt>();
        await RecoveryProcessPause.WaitForKillAsync(store);
    }
}
