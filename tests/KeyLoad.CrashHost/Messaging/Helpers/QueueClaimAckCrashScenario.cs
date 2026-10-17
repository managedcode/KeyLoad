using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

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
        ReplicatedOperation operation;
        if (ack)
        {
            var delivery = CrashDatabase.Submit(database, OperationKind.Receive, request, request.RequestId)
                .Get<ReceiveResult>().Deliveries.Single();
            await File.WriteAllBytesAsync(Path.Combine(directory, QueueClaimAckCrashProtocol.DeliveryFile),
                NativeSerialization.Serialize(delivery));
            var command = new DeliveryCommand(QueueClaimAckCrashProtocol.OperationId, request.Lane, delivery.Token, DeliveryAction.Ack);
            operation = database.NormalizeOperation(CrashDatabase.Operation(OperationKind.Delivery, command, command.CommandId));
        }
        else
        {
            operation = database.NormalizeOperation(CrashDatabase.Operation(OperationKind.Receive, request, request.RequestId));
        }
        await File.WriteAllBytesAsync(Path.Combine(directory, QueueClaimAckCrashProtocol.OperationFile),
            NativeSerialization.Serialize(operation));
        var placement = database.ReadAtomicPartitionPlacement(CrashFixtureValues.Principal,
            new(QueueClaimAckCrashProtocol.PlacementVersion, QueueClaimAckCrashProtocol.Partition));
        var target = new CommitToken(placement.Incarnation, QueueClaimAckCrashProtocol.Partition.AtomicPartitionId,
            checked(store.Position + QueueClaimAckCrashProtocol.AdjacentPosition), placement.PlacementEpoch);
        await File.WriteAllBytesAsync(Path.Combine(directory, QueueClaimAckCrashProtocol.BaselineFile),
            NativeSerialization.Serialize(CaptureBaseline(store)));
        await File.WriteAllBytesAsync(Path.Combine(directory, QueueClaimAckCrashProtocol.TargetFile),
            NativeSerialization.Serialize(target));
        boundary.Position = target.Position;
        boundary.Armed = true;
        _ = database.Apply(operation);
        await CrashHostPause.WaitForKillAsync();
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
