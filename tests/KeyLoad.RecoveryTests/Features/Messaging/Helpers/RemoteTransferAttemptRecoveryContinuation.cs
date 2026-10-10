using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.CrashHost;
using KeyLoad.CrashHost.Features.Messaging;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class RemoteTransferAttemptRecoveryContinuation
{
    internal static async Task<RemoteTransferAttemptRecovered> CompleteAsync(ZoneTreeStore store, DatabaseEngine database,
        CommandRequest advance, CommandRequest failedAccept, byte[] failedBytes, CancellationToken token)
    {
        var mutation = (AdvanceQueueTransferAttempt)advance.Mutations.Single();
        var source = RemoteTransferAttemptCrashScenario.Source;
        var destination = RemoteTransferAttemptCrashScenario.Destination;
        var original = database.InspectQueueTransfer(CrashFixtureValues.Principal, source, mutation.TransferId, token)!;
        var hint = RemoteTransferPendingDiscovery.Read(database, CrashFixtureValues.Principal, null, token).Hint
            ?? throw new InvalidOperationException(RemoteTransferAttemptCrashProtocol.Missing);
        await Assert.That(hint.AcceptGeneration).IsEqualTo(RemoteTransferAttemptCrashProtocol.AdvancedGeneration);
        var accept = new CommandRequest(RemoteTransferAttemptIdentity.AcceptId(hint, hint.AcceptGeneration),
            destination.Partition, [new AcceptQueueTransfer(destination, original.IntentToken)]);
        var accepted = database.ApplyEmbedded(CrashDatabase.Operation(OperationKind.Batch, accept, accept.CommandId), token).Get<CommitReceipt>();
        var proof = database.InspectQueueTransferReceipt(CrashFixtureValues.Principal, destination, source, mutation.TransferId, token)!;
        await Assert.That(accepted.Token).IsEqualTo(proof.TargetCommit);
        var complete = new CommandRequest(RemoteTransferCoordinationIdentity.CommandId(hint, RemoteTransferCoordinationProtocol.CompleteStage),
            source.Partition, [new CompleteQueueTransfer(source, mutation.TransferId, proof.ReceiptToken)]);
        var completed = database.ApplyEmbedded(CrashDatabase.Operation(OperationKind.Batch, complete, complete.CommandId), token).Get<CommitReceipt>();
        var delivered = database.InspectQueueTransfer(CrashFixtureValues.Principal, source, mutation.TransferId, token)!;
        await EqualAsync(delivered, original with { State = QueueTransferState.Delivered, ReceiptToken = proof.ReceiptToken });
        var message = database.InspectMessage(CrashFixtureValues.Principal, destination, RemoteTransferAttemptCrashProtocol.OriginalMessage)!;
        await EqualAsync(message, new MessageInspection(new(RemoteTransferAttemptCrashProtocol.OriginalMessage,
            MessageState.Ready, RemoteTransferAttemptCrashProtocol.InitialAttempts,
            RemoteTransferAttemptCrashProtocol.OriginalGeneration, RemoteTransferAttemptCrashProtocol.AdvancedGeneration, null, null),
            RemoteTransferAttemptCrashProtocol.Payload, RemoteTransferAttemptCrashProtocol.EmptyHeaders));
        await FailureAsync(store, database, failedAccept, failedBytes, token);
        return new(accept, accepted, complete, completed, delivered, proof, message);
    }

    internal static async Task ColdAsync(string root, ReplicatedOperation operation, CommitReceipt advanced,
        CommandRequest failedAccept, byte[] failedBytes, RemoteTransferAttemptRecovered result, CancellationToken token)
    {
        using var store = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        var database = RemoteTransferAttemptRecoveryDatabase.Open(store);
        await EqualAsync(database.ApplyEmbedded(operation, token).Get<CommitReceipt>(), advanced);
        await EqualAsync(database.ApplyEmbedded(CrashDatabase.Operation(OperationKind.Batch, result.Accept, result.Accept.CommandId), token).Get<CommitReceipt>(), result.Accepted);
        await EqualAsync(database.ApplyEmbedded(CrashDatabase.Operation(OperationKind.Batch, result.Complete, result.Complete.CommandId), token).Get<CommitReceipt>(), result.Completed);
        await EqualAsync(database.InspectQueueTransfer(CrashFixtureValues.Principal, RemoteTransferAttemptCrashScenario.Source,
            result.Proof.TransferId, token), result.Delivered);
        await EqualAsync(database.InspectQueueTransferReceipt(CrashFixtureValues.Principal, RemoteTransferAttemptCrashScenario.Destination,
            RemoteTransferAttemptCrashScenario.Source, result.Proof.TransferId, token), result.Proof);
        await EqualAsync(database.InspectMessage(CrashFixtureValues.Principal, RemoteTransferAttemptCrashScenario.Destination,
            RemoteTransferAttemptCrashProtocol.OriginalMessage), result.Message);
        await FailureAsync(store, database, failedAccept, failedBytes, token);
    }

    private static async Task FailureAsync(ZoneTreeStore store, DatabaseEngine database, CommandRequest request,
        byte[] original, CancellationToken token)
    {
        await Assert.That(database.ApplyEmbedded(CrashDatabase.Operation(OperationKind.Batch, request, request.CommandId), token).Error)
            .IsEqualTo(ErrorCode.ResourceExhausted);
        var bytes = store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(request.Partition,
            CrashFixtureValues.Principal, request.CommandId)))!;
        await Assert.That(bytes.AsSpan().SequenceEqual(original)).IsTrue();
    }

    private static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}
