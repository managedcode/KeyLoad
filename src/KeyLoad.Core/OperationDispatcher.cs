using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string OperationDispatcherBatchPlacementWitnessDoesNotMatchItsTransactionDetail = "The Batch placement witness does not match its transaction.";

    private const string UnsupportedOperationMessage = "The operation is unsupported.";
    private const string BatchEnvelopeMismatchMessage = "The envelope and command IDs differ.";
    private const string DispatchSystemSpaceName = "system";
    private const string DispatchPausedStateName = "dispatch-paused";

    private Result<object> Execute(IAtomicTransaction transaction, PrincipalRecord principal, Command<DatabaseCommand> operation,
        long position, AtomicPartitionPlacementResolution? placement)
        => operation.Value!.Kind switch
        {
            OperationKind.Bm25CorpusPublicationPhase => ExecuteBm25CorpusPublication(transaction, principal, operation, position),
            OperationKind.OnlineTextPublicationPhase => ExecuteOnlineTextPublication(transaction, principal, operation),
            OperationKind.Batch => ExecuteBatch(transaction, principal, operation, position, placement),
            OperationKind.Receive => Result(Receive(transaction, principal, Payload<ReceiveRequest>(operation), operation.Timestamp, position, operation)),
            OperationKind.Delivery => Result(CompleteDelivery(transaction, principal, Payload<DeliveryCommand>(operation), operation.Timestamp, position, operation)),
            OperationKind.CommitInbox => ExecuteTargetInbox(transaction, principal, operation, position),
            OperationKind.Processing => Result(CompleteProcessing(transaction, principal, Payload<ProcessingRequest>(operation), operation.Timestamp, position)),
            OperationKind.ConfigureSubscription => ExecuteConfigureSubscription(transaction, principal, operation),
            OperationKind.SeekSubscription => ExecuteSeekSubscription(transaction, principal, operation),
            OperationKind.SetSubscriptionPaused => ExecutePauseSubscription(transaction, operation),
            OperationKind.ReceiveSubscription => ExecuteReceiveSubscription(transaction, principal, operation, position),
            OperationKind.SubscriptionDelivery => ExecuteSubscriptionDelivery(transaction, principal, operation, position),
            OperationKind.SubscriptionProcessing => ExecuteSubscriptionProcessing(transaction, principal, operation, position),
            OperationKind.ConfigureProjectionConsumer => ExecuteConfigureProjection(transaction, operation),
            OperationKind.CommitProjectionBatch => ExecuteProjectionBatch(transaction, principal, operation, position),
            OperationKind.ReleaseProjectionConsumer => ExecuteReleaseProjection(transaction, operation),
            OperationKind.PurgeOutbox => ExecutePurgeOutbox(transaction, operation),
            OperationKind.ConfigureResource => ExecuteConfigureResource(transaction, operation),
            OperationKind.CreateTable => ExecuteCreateTable(transaction, operation),
            OperationKind.DropTable => ExecuteDropTable(transaction, operation, position),
            OperationKind.SetQueueDispatch => ExecuteQueueDispatchControl(transaction, operation),
            OperationKind.ConfigurePrincipal => ExecuteConfigurePrincipal(transaction, operation),
            OperationKind.ConfigureApiKey => ExecuteConfigureApiKey(transaction, operation),
            OperationKind.RuntimeJournal => Result(RuntimeJournal.Execute(transaction, principal, Payload<RuntimeJournalMutation>(operation))),
            OperationKind.SetDispatch => ExecuteDispatchState(transaction, operation),
            _ when BlobStorageOperations.Handles(operation.Value!.Kind)
                => new BlobStorageOperations(this).Execute(transaction, principal, operation, position),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, UnsupportedOperationMessage)
        };

    private Result<object> ExecuteBatch(IAtomicTransaction transaction, PrincipalRecord principal, Command<DatabaseCommand> operation,
        long position, AtomicPartitionPlacementResolution? placement)
    {
        var batch = Payload<CommandRequest>(operation);
        if (batch.CommandId != operation.CommandId)
        {
            throw Errors.Fail(ErrorCode.Validation, BatchEnvelopeMismatchMessage);
        }
        if (placement is null || placement.Partition != batch.Partition)
        {
            throw Errors.Fail(ErrorCode.Corruption, OperationDispatcherBatchPlacementWitnessDoesNotMatchItsTransactionDetail);
        }
        var token = new CommitToken(placement.Incarnation, batch.Partition.AtomicPartitionId, position);
        return Result(new CommitReceipt(batch.CommandId, token,
            ApplyMutations(transaction, principal, batch.Partition, batch.Mutations, operation.Timestamp, position,
                commitToken: token, operation: operation), Durability, AcknowledgedPersistedCopies));
    }

    private static Result<object> ExecuteDispatchState(IAtomicTransaction transaction, Command<DatabaseCommand> operation)
    {
        transaction.PutRecord(KeyCodec.Encode(DispatchSystemSpaceName, DispatchPausedStateName), Payload<bool>(operation));
        return Result(true);
    }
}
