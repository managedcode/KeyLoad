using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.CrashHost;
using KeyLoad.CrashHost.Features.Messaging;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class RemoteTransferRepairRecoveryContinuation
{
    internal static async Task<RemoteTransferRepairRecovered> CompleteAsync(ZoneTreeStore store, DatabaseEngine database,
        CommandRequest advance, CommandRequest failed, byte[] failedBytes, CancellationToken token)
    {
        var mutation = (AdvanceQueueTransferRepair)advance.Mutations.Single();
        var source = RemoteTransferAttemptCrashScenario.Source;
        var target = RemoteTransferAttemptCrashScenario.Destination;
        var intent = database.InspectQueueTransfer(CrashFixtureValues.Principal, source, mutation.TransferId, token)!;
        var hint = RemoteTransferPendingDiscovery.Read(database, CrashFixtureValues.Principal, null, token).Hint
            ?? throw new InvalidOperationException(RemoteTransferRepairCrashProtocol.Missing);
        var proof = database.InspectQueueTransferReceipt(CrashFixtureValues.Principal, target, source, mutation.TransferId, token);
        if (mutation.Stage == QueueTransferRepairStage.Accept)
        {
            await Assert.That(proof).IsNull();
            var accept = new CommandRequest(RemoteTransferRepairIdentity.CommandId(hint, QueueTransferRepairStage.Accept),
                target.Partition, [new AcceptQueueTransfer(target, intent.IntentToken)]);
            var accepted = database.ApplyEmbedded(CrashDatabase.Operation(OperationKind.Batch, accept, accept.CommandId), token).Get<CommitReceipt>();
            proof = database.InspectQueueTransferReceipt(CrashFixtureValues.Principal, target, source, mutation.TransferId, token)!;
            await Assert.That(proof.TargetCommit).IsEqualTo(accepted.Token);
        }
        if (proof is null)
        { throw new InvalidOperationException(RemoteTransferRepairCrashProtocol.Missing); }
        var complete = new CommandRequest(RemoteTransferRepairIdentity.CommandId(hint, QueueTransferRepairStage.Complete),
            source.Partition, [new CompleteQueueTransfer(source, mutation.TransferId, proof.ReceiptToken)]);
        var completed = database.ApplyEmbedded(CrashDatabase.Operation(OperationKind.Batch, complete, complete.CommandId), token).Get<CommitReceipt>();
        var delivered = database.InspectQueueTransfer(CrashFixtureValues.Principal, source, mutation.TransferId, token)!;
        await EqualAsync(delivered, intent with { State = QueueTransferState.Delivered, ReceiptToken = proof.ReceiptToken });
        var message = database.InspectMessage(CrashFixtureValues.Principal, target, RemoteTransferAttemptCrashProtocol.OriginalMessage)!;
        await EqualAsync(message, new MessageInspection(new(RemoteTransferAttemptCrashProtocol.OriginalMessage, MessageState.Ready,
            RemoteTransferAttemptCrashProtocol.InitialAttempts, RemoteTransferRepairCrashProtocol.InitialGeneration,
            RemoteTransferRepairCrashProtocol.InitialGeneration, null, null), RemoteTransferAttemptCrashProtocol.Payload,
            RemoteTransferAttemptCrashProtocol.EmptyHeaders));
        await FailureAsync(store, database, failed, failedBytes, token);
        return new(complete, completed, delivered, proof, message);
    }

    internal static async Task FailureAsync(ZoneTreeStore store, DatabaseEngine database, CommandRequest request,
        byte[] bytes, CancellationToken token)
    {
        await Assert.That(database.ApplyEmbedded(CrashDatabase.Operation(OperationKind.Batch, request, request.CommandId), token).Error)
            .IsEqualTo(ErrorCode.PermissionDenied);
        var retained = store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(request.Partition,
            CrashFixtureValues.Principal, request.CommandId))) ?? throw new InvalidOperationException(RemoteTransferRepairCrashProtocol.Missing);
        await Assert.That(retained.AsSpan().SequenceEqual(bytes)).IsTrue();
    }

    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}
