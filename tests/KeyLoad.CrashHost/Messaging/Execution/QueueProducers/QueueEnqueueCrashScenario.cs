using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class QueueEnqueueCrashScenario
{
    internal static async Task RunAsync(string directory, ZoneTreeStore store, CanonicalCrashBoundary boundary)
    {
        var database = CrashDatabase.Create(store, tenantId: QueueEnqueueCrashProtocol.Partition.TenantId);
        foreach (var resource in new[]
        {
            new ResourceDefinition(QueueEnqueueCrashProtocol.Queue, ResourceKind.WorkQueue, QueueEnqueueCrashProtocol.Partition.TransactionDomainId),
            new ResourceDefinition(QueueEnqueueCrashProtocol.Collection, ResourceKind.Collection, QueueEnqueueCrashProtocol.Partition.TransactionDomainId)
        })
        {
            _ = CrashDatabase.Submit(database, OperationKind.ConfigureResource, new ConfigureResourceRequest(
                QueueEnqueueCrashProtocol.Partition.TenantId, QueueEnqueueCrashProtocol.Partition.DatabaseId, resource), Guid.NewGuid()).Get<ResourceDefinition>();
        }
        var due = TimeProvider.System.GetUtcNow().AddHours(QueueEnqueueCrashProtocol.FutureHours);
        var command = new CommandRequest(QueueEnqueueCrashProtocol.OperationId, QueueEnqueueCrashProtocol.Partition,
            [new PutDocument(QueueEnqueueCrashProtocol.Collection, QueueEnqueueCrashProtocol.Ready, QueueEnqueueCrashProtocol.Payload),
                new EnqueueMessage(QueueEnqueueCrashProtocol.Queue, QueueEnqueueCrashProtocol.Ready, QueueEnqueueCrashProtocol.Payload, QueueEnqueueCrashProtocol.Headers),
                new EnqueueMessage(QueueEnqueueCrashProtocol.Queue, QueueEnqueueCrashProtocol.Scheduled, QueueEnqueueCrashProtocol.Payload,
                    QueueEnqueueCrashProtocol.Headers, NotBefore: due)]);
        var operation = database.OwnCommand(CrashDatabase.Operation(database, OperationKind.Batch, command, command.CommandId));
        var placement = database.ReadAtomicPartitionPlacement(CrashFixtureValues.Principal,
            QueueEnqueueCrashProtocol.Partition);
        var target = new CommitToken(placement.Incarnation, QueueEnqueueCrashProtocol.Partition.AtomicPartitionId,
            checked(store.Position + QueueEnqueueCrashProtocol.AdjacentPosition));
        await File.WriteAllBytesAsync(Path.Combine(directory, QueueEnqueueCrashProtocol.OperationFile), NativeSerialization.Serialize(operation));
        await File.WriteAllBytesAsync(Path.Combine(directory, QueueEnqueueCrashProtocol.TargetFile), NativeSerialization.Serialize(target));
        await File.WriteAllBytesAsync(Path.Combine(directory, QueueEnqueueCrashProtocol.BaselineFile), NativeSerialization.Serialize(Capture(store)));
        boundary.Position = target.Position;
        boundary.Armed = true;
        _ = database.Apply(operation);
        await CrashHostPause.WaitForKillAsync();
    }

    private static string[] Capture(ZoneTreeStore store) => store.Read(view =>
    {
        var page = view.Scan([], QueueEnqueueCrashProtocol.ImageRecords);
        if (page.HasMore)
        {
            throw new InvalidOperationException(QueueEnqueueCrashProtocol.BaselineExceeded);
        }
        return page.Records.Select(row => Convert.ToHexString(row.Key.Span) + QueueEnqueueCrashProtocol.ImageSeparator + Convert.ToHexString(row.Value.Span)).ToArray();
    });
}
