using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost.Features.Messaging;

internal static class RemoteTransferRepairCrashScenario
{
    internal static async Task RunAsync(string directory, ZoneTreeStore store, CanonicalCrashBoundary boundary,
        QueueTransferRepairStage stage)
    {
        var database = CrashDatabase.Create(store, tenantId: CrashFixtureValues.Tenant,
            configuredLimits: CrashExecutionOptions.RemoteTransferRepairLimits());
        RemoteTransferRepairCrashSetup.Configure(database);
        var source = RemoteTransferAttemptCrashScenario.Source;
        var target = RemoteTransferAttemptCrashScenario.Destination;
        var transfer = new CreateQueueTransfer(source, Guid.NewGuid(), target,
            new(target.Queue, RemoteTransferAttemptCrashProtocol.OriginalMessage, RemoteTransferAttemptCrashProtocol.Payload));
        var createId = Guid.NewGuid();
        _ = CrashDatabase.Submit(database, OperationKind.Batch,
            new CommandRequest(createId, source.Partition, [transfer]), createId).Get<CommitReceipt>();
        var intent = database.InspectQueueTransfer(CrashFixtureValues.Principal, source, transfer.TransferId)!;
        var hint = RemoteTransferPendingDiscovery.Read(database, CrashFixtureValues.Principal, null, CancellationToken.None).Hint
            ?? throw new InvalidOperationException(RemoteTransferRepairCrashProtocol.Missing);
        var failed = RemoteTransferRepairCrashSetup.Failed(database, hint, intent.IntentToken, stage);
        var failureBytes = store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(failed.Original.Partition,
            CrashFixtureValues.Principal, failed.Original.CommandId))) ?? throw new InvalidOperationException(RemoteTransferRepairCrashProtocol.Missing);
        var request = new RemoteTransferCoordinationReadRequest(RemoteTransferRepairProtocol.ReadPurpose, source, target,
            transfer.TransferId, intent.IntentToken, hint.AcceptGeneration, failed.Original.CommandId, stage,
            hint.AcceptPolicyGeneration, hint.CompleteGeneration, failed.Receipt);
        var witness = database.ReadRemoteTransferCoordination(CrashFixtureValues.Principal, request, CancellationToken.None).RepairWitness
            ?? throw new InvalidOperationException(RemoteTransferRepairCrashProtocol.Missing);
        var claims = database.Verify<RemoteTransferRepairClaims>(witness, database.Limits.MaxBatchBytes);
        var id = RemoteTransferRepairIdentity.AdvanceId(hint, stage, claims.OutcomeDigest);
        var command = new CommandRequest(id, source.Partition, [new AdvanceQueueTransferRepair(source,
            transfer.TransferId, stage, hint.AcceptGeneration, hint.AcceptPolicyGeneration, hint.CompleteGeneration, witness)]);
        var operation = CrashDatabase.Operation(OperationKind.Batch, command, command.CommandId);
        await File.WriteAllBytesAsync(Path.Combine(directory, RemoteTransferRepairCrashProtocol.OperationFile), JsonDefaults.Serialize(operation));
        await File.WriteAllBytesAsync(Path.Combine(directory, RemoteTransferRepairCrashProtocol.OriginalFile), JsonDefaults.Serialize(failed.Original));
        await File.WriteAllBytesAsync(Path.Combine(directory, RemoteTransferRepairCrashProtocol.FailureFile), failureBytes);
        boundary.Position = store.Position + RemoteTransferRepairCrashProtocol.Step;
        boundary.Armed = true;
        _ = database.Apply(operation).Get<CommitReceipt>();
        await CrashHostPause.WaitForKillAsync();
    }
}
