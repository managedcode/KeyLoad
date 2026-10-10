using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.CrashHost.Features.Messaging;

internal static class RemoteTransferRepairCrashSetup
{
    internal static void Configure(DatabaseEngine database)
    {
        foreach (var lane in new[] { RemoteTransferAttemptCrashScenario.Source, RemoteTransferAttemptCrashScenario.Destination })
        {
            var resource = new ResourceDefinition(lane.Queue, ResourceKind.WorkQueue, lane.Partition.TransactionDomainId);
            _ = CrashDatabase.Submit(database, OperationKind.ConfigureResource,
                new ConfigureResourceRequest(lane.Partition.TenantId, lane.Partition.DatabaseId, resource), Guid.NewGuid()).Get<ResourceDefinition>();
        }
    }

    internal static void Policy(DatabaseEngine database, QueueLaneRef denied, bool allow)
    {
        var old = database.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(CrashFixtureValues.Principal)))
            ?? throw new InvalidOperationException(RemoteTransferRepairCrashProtocol.Missing);
        var source = RemoteTransferAttemptCrashScenario.Source;
        var target = RemoteTransferAttemptCrashScenario.Destination;
        var updated = old with
        {
            Grants = [new(source.Partition.DatabaseId, source.Queue, denied == source && !allow ? Capability.QueueInspect : Capability.All),
                new(target.Partition.DatabaseId, target.Queue, denied == target && !allow ? Capability.QueueInspect : Capability.All)],
            PolicyEpoch = checked(old.PolicyEpoch + RemoteTransferRepairCrashProtocol.Step)
        };
        _ = CrashDatabase.Submit(database, OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(updated), Guid.NewGuid()).Get<PrincipalRecord>();
    }

    internal static (CommandRequest Original, string? Receipt) Failed(DatabaseEngine database,
        RemoteTransferCoordinationHint hint, string intent, QueueTransferRepairStage stage)
    {
        var source = RemoteTransferAttemptCrashScenario.Source;
        var target = RemoteTransferAttemptCrashScenario.Destination;
        string? receipt = null;
        if (stage == QueueTransferRepairStage.Complete)
        {
            var id = Guid.NewGuid();
            _ = CrashDatabase.Submit(database, OperationKind.Batch,
                new CommandRequest(id, target.Partition, [new AcceptQueueTransfer(target, intent)]), id).Get<CommitReceipt>();
            receipt = database.InspectQueueTransferReceipt(CrashFixtureValues.Principal, target, source, hint.TransferId)!.ReceiptToken;
        }
        Policy(database, stage == QueueTransferRepairStage.Accept ? target : source, allow: false);
        var originalId = RemoteTransferRepairIdentity.CommandId(hint, stage);
        Mutation mutation = stage == QueueTransferRepairStage.Accept ? new AcceptQueueTransfer(target, intent)
            : new CompleteQueueTransfer(source, hint.TransferId, receipt!);
        var original = new CommandRequest(originalId, stage == QueueTransferRepairStage.Accept ? target.Partition : source.Partition, [mutation]);
        var failed = CrashDatabase.Submit(database, OperationKind.Batch, original, originalId);
        if (failed.Error != ErrorCode.PermissionDenied || failed.Json is not null || failed.NativeValue is not null)
        { throw new InvalidOperationException(RemoteTransferRepairCrashProtocol.Missing); }
        Policy(database, stage == QueueTransferRepairStage.Accept ? target : source, allow: true);
        return (original, receipt);
    }
}
