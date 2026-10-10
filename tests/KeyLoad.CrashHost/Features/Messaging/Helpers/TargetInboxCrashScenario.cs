using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
namespace KeyLoad.CrashHost.Features.Messaging;

internal static class TargetInboxCrashScenario
{
    internal static PartitionRef Target { get; } = new(CrashFixtureValues.Tenant, CrashFixtureValues.Database,
        CrashFixtureValues.Orders, TargetInboxCrashProtocol.TargetKey);
    internal static QueueLaneRef Source { get; } = new(Target with { PartitionKey = TargetInboxCrashProtocol.SourceKey }, TargetInboxCrashProtocol.Input);
    internal static async Task RunAsync(string directory, ZoneTreeStore store, CanonicalCrashBoundary boundary)
    {
        var database = CrashDatabase.Create(store);
        Configure(database);
        var produce = Guid.NewGuid();
        _ = CrashDatabase.Submit(database, OperationKind.Batch, new CommandRequest(produce, Source.Partition,
            [new EnqueueMessage(Source.Queue, TargetInboxCrashProtocol.Message, TargetInboxCrashProtocol.Payload)]), produce).Get<CommitReceipt>();
        var receive = new ReceiveRequest(Guid.NewGuid(), Source);
        var delivery = CrashDatabase.Submit(database, OperationKind.Receive, receive, receive.RequestId).Get<ReceiveResult>().Deliveries.Single();
        var request = new CommitInboxRequest(Guid.Parse(TargetInboxCrashProtocol.Command), new(Target, TargetInboxCrashProtocol.Target),
            Source, delivery.Id, delivery.DeliveryGeneration, TargetInboxCrashProtocol.Handler, TargetInboxCrashProtocol.Generation,
            [new PutDocument(TargetInboxCrashProtocol.Collection, TargetInboxCrashProtocol.Effect, TargetInboxCrashProtocol.Payload,
                TargetInboxCrashProtocol.EmptyRevision),
             new EnqueueMessage(TargetInboxCrashProtocol.Output, TargetInboxCrashProtocol.Message, TargetInboxCrashProtocol.Payload)]);
        var operation = CrashDatabase.Operation(OperationKind.CommitInbox, request, request.CommandId);
        await File.WriteAllBytesAsync(Path.Combine(directory, TargetInboxCrashProtocol.OperationFile), JsonDefaults.Serialize(operation));
        await File.WriteAllBytesAsync(Path.Combine(directory, TargetInboxCrashProtocol.DeliveryFile), JsonDefaults.Serialize(delivery));
        boundary.Position = store.Position + TargetInboxCrashProtocol.PositionStep;
        boundary.Armed = true;
        _ = database.Apply(operation).Get<CommitInboxResult>();
        await CrashHostPause.WaitForKillAsync();
    }
    private static void Configure(DatabaseEngine database)
    {
        foreach (var name in new[] { TargetInboxCrashProtocol.Input, TargetInboxCrashProtocol.Output, TargetInboxCrashProtocol.Target })
        {
            var resource = new ResourceDefinition(name, ResourceKind.WorkQueue, Target.TransactionDomainId);
            if (name == TargetInboxCrashProtocol.Target)
            { resource = resource with { InboxPolicy = new(TargetInboxCrashProtocol.ReceiptLimit, TargetInboxCrashProtocol.ByteLimit) }; }
            _ = CrashDatabase.Submit(database, OperationKind.ConfigureResource,
                new ConfigureResourceRequest(Target.TenantId, Target.DatabaseId, resource), Guid.NewGuid()).Get<ResourceDefinition>();
        }
        _ = CrashDatabase.Submit(database, OperationKind.ConfigureResource,
            new ConfigureResourceRequest(Target.TenantId, Target.DatabaseId,
                new(TargetInboxCrashProtocol.Collection, ResourceKind.Collection, Target.TransactionDomainId)), Guid.NewGuid()).Get<ResourceDefinition>();
    }
}
