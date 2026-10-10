using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost.Features.Messaging;

internal static class RemoteTransferAttemptCrashScenario
{
    internal static QueueLaneRef Source { get; } = new(new(CrashFixtureValues.Tenant, CrashFixtureValues.Database,
        CrashFixtureValues.Orders, RemoteTransferAttemptCrashProtocol.SourceKey), RemoteTransferAttemptCrashProtocol.SourceQueue);
    internal static QueueLaneRef Destination { get; } = new(Source.Partition with
    { PartitionKey = RemoteTransferAttemptCrashProtocol.DestinationKey }, RemoteTransferAttemptCrashProtocol.DestinationQueue);

    internal static async Task RunAsync(string directory, ZoneTreeStore store, CanonicalCrashBoundary boundary)
    {
        var database = CrashDatabase.Create(store, tenantId: CrashFixtureValues.Tenant,
            configuredLimits: CrashExecutionOptions.RemoteTransferAttemptLimits());
        RemoteTransferAttemptCrashSetup.Configure(database);
        var transfer = new CreateQueueTransfer(Source, Guid.NewGuid(), Destination,
            new(Destination.Queue, RemoteTransferAttemptCrashProtocol.OriginalMessage, RemoteTransferAttemptCrashProtocol.Payload));
        var createId = Guid.NewGuid();
        _ = CrashDatabase.Submit(database, OperationKind.Batch, new CommandRequest(createId, Source.Partition, [transfer]), createId).Get<CommitReceipt>();
        var original = database.InspectQueueTransfer(CrashFixtureValues.Principal, Source, transfer.TransferId)!;
        var hint = RemoteTransferPendingDiscovery.Read(database, CrashFixtureValues.Principal, null,
            CancellationToken.None).Hint ?? throw new InvalidOperationException(RemoteTransferAttemptCrashProtocol.Missing);
        var accept = new CommandRequest(RemoteTransferAttemptIdentity.AcceptId(hint, hint.AcceptGeneration),
            Destination.Partition, [new AcceptQueueTransfer(Destination, original.IntentToken)]);
        var failed = CrashDatabase.Submit(database, OperationKind.Batch, accept, accept.CommandId);
        if (failed.Error != ErrorCode.ResourceExhausted)
        { throw new InvalidOperationException(RemoteTransferAttemptCrashProtocol.Missing); }
        var failureBytes = store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(Destination.Partition,
            CrashFixtureValues.Principal, accept.CommandId))) ?? throw new InvalidOperationException(RemoteTransferAttemptCrashProtocol.Missing);
        RemoteTransferAttemptCrashSetup.AckFiller(database);
        var read = new RemoteTransferCoordinationReadRequest(RemoteTransferAttemptProtocol.FailureReadPurpose, Source,
            Destination, transfer.TransferId, original.IntentToken, hint.AcceptGeneration, accept.CommandId);
        var witness = database.ReadRemoteTransferCoordination(CrashFixtureValues.Principal, read, CancellationToken.None).FailureWitness
            ?? throw new InvalidOperationException(RemoteTransferAttemptCrashProtocol.Missing);
        var claims = database.Verify<RemoteTransferAcceptFailureClaims>(witness, database.Limits.MaxBatchBytes);
        var advance = new CommandRequest(RemoteTransferAttemptIdentity.AdvanceId(hint, hint.AcceptGeneration, claims.OutcomeDigest),
            Source.Partition, [new AdvanceQueueTransferAttempt(Source, transfer.TransferId, hint.AcceptGeneration, witness)]);
        var operation = CrashDatabase.Operation(OperationKind.Batch, advance, advance.CommandId);
        await File.WriteAllBytesAsync(Path.Combine(directory, RemoteTransferAttemptCrashProtocol.OperationFile), JsonDefaults.Serialize(operation));
        await File.WriteAllBytesAsync(Path.Combine(directory, RemoteTransferAttemptCrashProtocol.AcceptFile), JsonDefaults.Serialize(accept));
        await File.WriteAllBytesAsync(Path.Combine(directory, RemoteTransferAttemptCrashProtocol.FailureFile), failureBytes);
        boundary.Position = store.Position + RemoteTransferAttemptCrashProtocol.PositionStep;
        boundary.Armed = true;
        _ = database.Apply(operation).Get<CommitReceipt>();
        await CrashHostPause.WaitForKillAsync();
    }
}
