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
    internal static Result<object> Execute(this DatabaseEngine engine, IAtomicTransaction transaction, PrincipalRecord principal, Command<DatabaseCommand> operation, long position, AtomicPartitionPlacementResolution? placement, CancellationToken cancellationToken) => operation.Value!.Kind switch
    {
        OperationKind.Bm25CorpusPublicationPhase => engine.ExecuteBm25CorpusPublication(transaction, principal, operation, position),
        OperationKind.OnlineTextPublicationPhase => engine.ExecuteOnlineTextPublication(transaction, principal, operation),
        OperationKind.FileMetadata => FileDispatch.ExecuteFileMetadata(transaction, principal, OperationProtocol.Payload<global::KeyLoad.Core.Features.BlobStorage.FileMetadataMutation>(operation), operation.Timestamp),
        OperationKind.Batch => engine.ExecuteBatch(transaction, principal, operation, position, placement),
        OperationKind.Receive => OperationProtocol.Result(engine.Receive(transaction, principal, OperationProtocol.Payload<ReceiveRequest>(operation), operation.Timestamp, position, operation)),
        OperationKind.Delivery => OperationProtocol.Result(engine.CompleteDelivery(transaction, principal, OperationProtocol.Payload<DeliveryCommand>(operation), operation.Timestamp, position, operation)),
        OperationKind.CommitInbox => engine.ExecuteTargetInbox(transaction, principal, operation, position),
        OperationKind.Processing => OperationProtocol.Result(engine.CompleteProcessing(transaction, principal, OperationProtocol.Payload<ProcessingRequest>(operation), operation.Timestamp, position)),
        OperationKind.ConfigureSubscription => engine.ExecuteConfigureSubscription(transaction, principal, operation),
        OperationKind.SeekSubscription => engine.ExecuteSeekSubscription(transaction, principal, operation),
        OperationKind.SetSubscriptionPaused => engine.ExecutePauseSubscription(transaction, operation),
        OperationKind.ReceiveSubscription => engine.ExecuteReceiveSubscription(transaction, principal, operation, position),
        OperationKind.SubscriptionDelivery => engine.ExecuteSubscriptionDelivery(transaction, principal, operation, position),
        OperationKind.SubscriptionProcessing => engine.ExecuteSubscriptionProcessing(transaction, principal, operation, position),
        OperationKind.ConfigureProjectionConsumer => engine.ExecuteConfigureProjection(transaction, operation),
        OperationKind.CommitProjectionBatch => engine.ExecuteProjectionBatch(transaction, principal, operation, position),
        OperationKind.ReleaseProjectionConsumer => ChangeFeedsCommands.ExecuteReleaseProjection(transaction, operation),
        OperationKind.PurgeOutbox => engine.ExecutePurgeOutbox(transaction, operation),
        OperationKind.ConfigureResource => engine.ExecuteConfigureResource(transaction, operation),
        OperationKind.CreateTable => RelationalStorageCommands.ExecuteCreateTable(transaction, operation),
        OperationKind.DropTable => engine.ExecuteDropTable(transaction, operation, position, cancellationToken),
        OperationKind.SetQueueDispatch => QueueDispatch.ExecuteQueueDispatchControl(transaction, operation),
        OperationKind.ConfigurePrincipal => PrincipalConfiguration.ExecuteConfigurePrincipal(transaction, operation),
        OperationKind.ConfigureApiKey => engine.ExecuteConfigureApiKey(transaction, operation),
        OperationKind.RuntimeJournal => OperationProtocol.Result(engine.RuntimeJournal.Execute(transaction, principal, OperationProtocol.Payload<RuntimeJournalMutation>(operation))),
        OperationKind.SetDispatch => ExecuteDispatchState(transaction, operation),
        _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, UnsupportedOperationMessage)
    };
    internal static Result<object> ExecuteBatch(this DatabaseEngine engine, IAtomicTransaction transaction, PrincipalRecord principal, Command<DatabaseCommand> operation, long position, AtomicPartitionPlacementResolution? placement)
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
        return OperationProtocol.Result(new CommitReceipt(batch.CommandId, token, engine.ApplyMutations(transaction, principal, batch.Partition, batch.Mutations, operation.Timestamp, position, commitToken: token, operation: operation), engine.Durability, engine.AcknowledgedPersistedCopies));
    }

    internal static Result<object> ExecuteDispatchState(IAtomicTransaction transaction, Command<DatabaseCommand> operation)
    {
        transaction.PutRecord(KeyCodec.Encode(DispatchSystemSpaceName, DispatchPausedStateName), OperationProtocol.Payload<bool>(operation));
        return OperationProtocol.Result(true);
    }
}
