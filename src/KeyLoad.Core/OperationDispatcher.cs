using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes OperationDispatcher operations against the caller's database context.</summary>
public static class OperationDispatcher
{
    internal const string OperationDispatcherBatchPlacementWitnessDoesNotMatchItsTransactionDetail = "The Batch placement witness does not match its transaction.";
    internal const string UnsupportedOperationMessage = "The operation is unsupported.";
    internal const string BatchEnvelopeMismatchMessage = "The envelope and command IDs differ.";
    internal const string DispatchSystemSpaceName = "system";
    internal const string DispatchPausedStateName = "dispatch-paused";
    internal static Result<object> /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */ Execute(this DatabaseEngine engine, IAtomicTransaction transaction, PrincipalRecord principal, Command<DatabaseCommand> operation, long position, AtomicPartitionPlacementResolution? placement, CancellationToken cancellationToken) => operation.Value!.Kind switch
    {
        OperationKind.Bm25CorpusPublicationPhase => OperationProtocol.Result(engine.ExecuteBm25CorpusPublication(transaction, principal, operation, position)) /* KLD0041: The dispatcher places the typed business outcome in the existing native mixed-operation stored-result schema. */,
        OperationKind.OnlineTextPublicationPhase => OperationProtocol.Result(engine.ExecuteOnlineTextPublication(transaction, principal, operation)) /* KLD0041: The dispatcher places the typed business outcome in the existing native mixed-operation stored-result schema. */,
        OperationKind.FileMetadata => FileDispatch.ExecuteFileMetadata(transaction, principal, OperationProtocol.Payload<global::KeyLoad.Core.BlobStorage.FileMetadataMutation>(operation), operation.Timestamp) /* KLD0041: The dispatcher places the typed business outcome in the existing native mixed-operation stored-result schema. */,
        OperationKind.Batch => OperationProtocol.Result(engine.ExecuteBatch(transaction, principal, operation, position, placement)) /* KLD0041: The dispatcher places the typed business outcome in the existing native mixed-operation stored-result schema. */,
        OperationKind.Receive => OperationProtocol.Result(engine.Receive(transaction, principal, OperationProtocol.Payload<ReceiveRequest>(operation), operation.Timestamp, position, operation)) /* KLD0041: The dispatcher places the typed business outcome in the existing native mixed-operation stored-result schema. */,
        OperationKind.Delivery => OperationProtocol.Result(engine.CompleteDelivery(transaction, principal, OperationProtocol.Payload<DeliveryCommand>(operation), operation.Timestamp, position, operation)) /* KLD0041: The dispatcher places the typed business outcome in the existing native mixed-operation stored-result schema. */,
        OperationKind.CommitInbox => OperationProtocol.Result(engine.ExecuteTargetInbox(transaction, principal, operation, position)) /* KLD0041: The dispatcher places the typed business outcome in the existing native mixed-operation stored-result schema. */,
        OperationKind.Processing => OperationProtocol.Result(engine.CompleteProcessing(transaction, principal, OperationProtocol.Payload<ProcessingRequest>(operation), operation.Timestamp, position)) /* KLD0041: The dispatcher places the typed business outcome in the existing native mixed-operation stored-result schema. */,
        OperationKind.ConfigureSubscription => OperationProtocol.Result(engine.ExecuteConfigureSubscription(transaction, principal, operation)) /* KLD0041: The dispatcher places the typed business outcome in the existing native mixed-operation stored-result schema. */,
        OperationKind.SeekSubscription => OperationProtocol.Result(engine.ExecuteSeekSubscription(transaction, principal, operation)) /* KLD0041: The dispatcher places the typed business outcome in the existing native mixed-operation stored-result schema. */,
        OperationKind.SetSubscriptionPaused => OperationProtocol.Result(engine.ExecutePauseSubscription(transaction, operation)) /* KLD0041: The dispatcher places the typed business outcome in the existing native mixed-operation stored-result schema. */,
        OperationKind.ReceiveSubscription => OperationProtocol.Result(engine.ExecuteReceiveSubscription(transaction, principal, operation, position)) /* KLD0041: The dispatcher places the typed business outcome in the existing native mixed-operation stored-result schema. */,
        OperationKind.SubscriptionDelivery => OperationProtocol.Result(engine.ExecuteSubscriptionDelivery(transaction, principal, operation, position)) /* KLD0041: The dispatcher places the typed business outcome in the existing native mixed-operation stored-result schema. */,
        OperationKind.SubscriptionProcessing => OperationProtocol.Result(engine.ExecuteSubscriptionProcessing(transaction, principal, operation, position)) /* KLD0041: The dispatcher places the typed business outcome in the existing native mixed-operation stored-result schema. */,
        OperationKind.ConfigureProjectionConsumer => OperationProtocol.Result(engine.ExecuteConfigureProjection(transaction, operation)) /* KLD0041: The dispatcher places the typed business outcome in the existing native mixed-operation stored-result schema. */,
        OperationKind.CommitProjectionBatch => OperationProtocol.Result(engine.ExecuteProjectionBatch(transaction, principal, operation, position)) /* KLD0041: The dispatcher places the typed business outcome in the existing native mixed-operation stored-result schema. */,
        OperationKind.ReleaseProjectionConsumer => OperationProtocol.Result(ChangeFeedsCommands.ExecuteReleaseProjection(transaction, operation)) /* KLD0041: The dispatcher places the typed business outcome in the existing native mixed-operation stored-result schema. */,
        OperationKind.PurgeOutbox => OperationProtocol.Result(engine.ExecutePurgeOutbox(transaction, operation)) /* KLD0041: The dispatcher places the typed business outcome in the existing native mixed-operation stored-result schema. */,
        OperationKind.ConfigureResource => OperationProtocol.Result(engine.ExecuteConfigureResource(transaction, operation)) /* KLD0041: The dispatcher places the typed business outcome in the existing native mixed-operation stored-result schema. */,
        OperationKind.CreateTable => OperationProtocol.Result(RelationalStorageCommands.ExecuteCreateTable(transaction, operation)) /* KLD0041: The dispatcher places the typed business outcome in the existing native mixed-operation stored-result schema. */,
        OperationKind.DropTable => OperationProtocol.Result(engine.ExecuteDropTable(transaction, operation, position, cancellationToken)) /* KLD0041: The dispatcher places the typed business outcome in the existing native mixed-operation stored-result schema. */,
        OperationKind.SetQueueDispatch => OperationProtocol.Result(QueueDispatch.ExecuteQueueDispatchControl(transaction, operation)) /* KLD0041: The dispatcher places the typed business outcome in the existing native mixed-operation stored-result schema. */,
        OperationKind.ConfigurePrincipal => OperationProtocol.Result(PrincipalConfiguration.ExecuteConfigurePrincipal(transaction, operation)) /* KLD0041: The dispatcher places the typed business outcome in the existing native mixed-operation stored-result schema. */,
        OperationKind.ConfigureApiKey => OperationProtocol.Result(engine.ExecuteConfigureApiKey(transaction, operation)) /* KLD0041: The dispatcher places the typed business outcome in the existing native mixed-operation stored-result schema. */,
        OperationKind.RuntimeJournal => OperationProtocol.Result(engine.RuntimeJournal.Execute(transaction, principal, OperationProtocol.Payload<RuntimeJournalMutation>(operation))) /* KLD0041: The dispatcher places the typed business outcome in the existing native mixed-operation stored-result schema. */,
        OperationKind.SetDispatch => OperationProtocol.Result(ExecuteDispatchState(transaction, operation)) /* KLD0041: The dispatcher places the typed business outcome in the existing native mixed-operation stored-result schema. */,
        _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, UnsupportedOperationMessage)
    };
    internal static CommitReceipt ExecuteBatch(this DatabaseEngine engine, IAtomicTransaction transaction, PrincipalRecord principal, Command<DatabaseCommand> operation, long position, AtomicPartitionPlacementResolution? placement)
    {
        var batch = OperationProtocol.Payload<CommandRequest>(operation);
        if (batch.CommandId != operation.CommandId)
        {
            throw Errors.Fail(ErrorCode.Validation, BatchEnvelopeMismatchMessage);
        }

        if (placement is null || placement.Partition != batch.Partition)
        {
            throw Errors.Fail(ErrorCode.Corruption, OperationDispatcherBatchPlacementWitnessDoesNotMatchItsTransactionDetail);
        }

        var token = new CommitToken(placement.Incarnation, batch.Partition.AtomicPartitionId, position);
        return new CommitReceipt(batch.CommandId, token, engine.ApplyMutations(transaction, principal, batch.Partition, batch.Mutations, operation.Timestamp, position, commitToken: token, operation: operation), engine.Durability, engine.AcknowledgedPersistedCopies);
    }

    internal static bool ExecuteDispatchState(IAtomicTransaction transaction, Command<DatabaseCommand> operation)
    {
        transaction.PutRecord(KeyCodec.Encode(DispatchSystemSpaceName, DispatchPausedStateName), OperationProtocol.Payload<bool>(operation));
        return true;
    }
}
