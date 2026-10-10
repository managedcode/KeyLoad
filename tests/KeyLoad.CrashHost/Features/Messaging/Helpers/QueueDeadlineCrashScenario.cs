using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class QueueDeadlineCrashScenario
{
    internal static async Task RunAsync(string directory, ZoneTreeStore store, CanonicalCrashBoundary boundary)
    {
        var database = CrashDatabase.Create(store, tenantId: QueueDeadlineCrashProtocol.Partition.TenantId);
        CrashDatabase.Submit(database, OperationKind.ConfigureResource, new ConfigureResourceRequest(
            QueueDeadlineCrashProtocol.Partition.TenantId, QueueDeadlineCrashProtocol.Partition.DatabaseId,
            new(QueueDeadlineCrashProtocol.Queue, ResourceKind.WorkQueue,
                QueueDeadlineCrashProtocol.Partition.TransactionDomainId)), Guid.NewGuid()).Get<ResourceDefinition>();
        var expiry = TimeProvider.System.GetUtcNow().AddSeconds(QueueDeadlineCrashProtocol.BusinessLifetimeSeconds);
        var original = new CommandRequest(Guid.NewGuid(), QueueDeadlineCrashProtocol.Partition,
            [new EnqueueMessage(QueueDeadlineCrashProtocol.Queue, QueueDeadlineCrashProtocol.Message,
                QueueDeadlineCrashProtocol.Payload, QueueDeadlineCrashProtocol.Headers, ExpiresAt: expiry)]);
        CrashDatabase.Submit(database, OperationKind.Batch, original, original.CommandId).Get<CommitReceipt>();
        var remaining = expiry - TimeProvider.System.GetUtcNow();
        if (remaining > TimeSpan.Zero)
        { await Task.Delay(remaining, TimeProvider.System); }
        var request = new CommandRequest(Guid.NewGuid(), QueueDeadlineCrashProtocol.Partition,
            [new AdvanceQueueDeadline(QueueDeadlineCrashProtocol.Queue, QueueDeadlineCrashProtocol.Message,
                MessageState.Ready, QueueDeadlineCrashProtocol.First, QueueDeadlineCrashProtocol.Initial,
                expiry, QueueDeadlineKind.ExpireMessage)]);
        var operation = database.NormalizeOperation(CrashDatabase.Operation(OperationKind.Batch, request, request.CommandId));
        await File.WriteAllBytesAsync(Path.Combine(directory, QueueDeadlineCrashProtocol.OperationFile), JsonDefaults.Serialize(operation));
        boundary.Position = store.Position + QueueDeadlineCrashProtocol.First;
        boundary.Armed = true;
        database.Apply(operation).Get<CommitReceipt>();
        await CrashHostPause.WaitForKillAsync();
    }
}
