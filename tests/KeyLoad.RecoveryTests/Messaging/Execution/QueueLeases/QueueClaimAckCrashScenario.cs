using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;

namespace KeyLoad.RecoveryTests;

internal static class QueueClaimAckCrashScenario
{
    internal static async Task RunAsync(string directory, ZoneTreeStore store, CanonicalCrashBoundary boundary, bool ack)
    {
        var database = CrashDatabase.Create(store, tenantId: QueueClaimAckCrashProtocol.Partition.TenantId);
        _ = CrashDatabase.Submit(database, OperationKind.ConfigureResource, new ConfigureResourceRequest(
            QueueClaimAckCrashProtocol.Partition.TenantId, QueueClaimAckCrashProtocol.Partition.DatabaseId,
            new ResourceDefinition(QueueClaimAckCrashProtocol.Queue, ResourceKind.WorkQueue,
                QueueClaimAckCrashProtocol.Partition.TransactionDomainId)), Guid.NewGuid()).Get<ResourceDefinition>();
        var id = Guid.NewGuid();
        _ = CrashDatabase.Submit(database, OperationKind.Batch, new CommandRequest(id, QueueClaimAckCrashProtocol.Partition,
            [new EnqueueMessage(QueueClaimAckCrashProtocol.Queue, QueueClaimAckCrashProtocol.Message,
                QueueClaimAckCrashProtocol.Payload, QueueClaimAckCrashProtocol.Headers)]), id).Get<CommitReceipt>();
        var request = new ReceiveRequest(ack ? Guid.NewGuid() : QueueClaimAckCrashProtocol.OperationId,
            QueueClaimAckCrashProtocol.Lane, MaxMessages: QueueClaimAckCrashProtocol.MaximumClaimMessages, LeaseSeconds: QueueClaimAckCrashProtocol.LeaseSeconds);
        Command<DatabaseCommand> operation;
        if (ack)
        {
            var delivery = CrashDatabase.Submit(database, OperationKind.Receive, request, request.RequestId)
                .Get<ReceiveResult>().Deliveries.Single();
            await File.WriteAllBytesAsync(Path.Combine(directory, QueueClaimAckCrashProtocol.DeliveryFile),
                OrleansSerialization.Serialize(delivery));
            var command = new DeliveryCommand(QueueClaimAckCrashProtocol.OperationId, request.Lane, delivery.Token, DeliveryAction.Ack);
            operation = database.OwnCommand(CrashDatabase.Operation(database, OperationKind.Delivery, command, command.CommandId));
        }
        else
        {
            operation = database.OwnCommand(CrashDatabase.Operation(database, OperationKind.Receive, request, request.RequestId));
        }
        await File.WriteAllBytesAsync(Path.Combine(directory, QueueClaimAckCrashProtocol.OperationFile),
            OrleansSerialization.Serialize(operation));
        var target = CrashDatabase.NextCommitToken(database, QueueClaimAckCrashProtocol.Partition);
        var physicalTarget = checked(store.Position + QueueClaimAckCrashProtocol.AdjacentPosition);
        await File.WriteAllBytesAsync(Path.Combine(directory, QueueClaimAckCrashProtocol.BaselineFile),
            OrleansSerialization.Serialize(CaptureBaseline(store)));
        await File.WriteAllBytesAsync(Path.Combine(directory, QueueClaimAckCrashProtocol.TargetFile),
            OrleansSerialization.Serialize(target));
        await File.WriteAllBytesAsync(Path.Combine(directory, QueueClaimAckCrashProtocol.PhysicalTargetFile),
            OrleansSerialization.Serialize(physicalTarget));
        boundary.Position = physicalTarget;
        boundary.Armed = true;
        _ = database.Apply(operation);
        await RecoveryProcessPause.WaitForKillAsync();
    }
    private static string[] CaptureBaseline(ZoneTreeStore store) => store.Read(view =>
    {
        var page = view.Scan([], QueueClaimAckCrashProtocol.ImageRecords);
        if (page.HasMore)
        {
            throw new InvalidOperationException(QueueClaimAckCrashProtocol.BaselineBoundExceeded);
        }
        return page.Records.Select(row => Convert.ToHexString(row.Key.Span) + QueueClaimAckCrashProtocol.ImageRowSeparator + Convert.ToHexString(row.Value.Span)).ToArray();
    });
}
