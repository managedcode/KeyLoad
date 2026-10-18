using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string OperationDispatcherBatchPlacementWitnessDoesNotMatchItsTransactionDetail = "The Batch placement witness does not match its transaction.";

    private const string UnsupportedOperationMessage = "The operation is unsupported.";
    private const string BatchEnvelopeMismatchMessage = "The envelope and command IDs differ.";
    private const string DispatchSystemSpaceName = "system";
    private const string DispatchPausedStateName = "dispatch-paused";

    private OperationResult Execute(IAtomicTransaction transaction, PrincipalRecord principal, ReplicatedOperation operation,
        long position, AtomicPartitionPlacementResolution? placement)
        => operation.Kind switch
        {
            OperationKind.Bm25CorpusPublicationPhase => ExecuteBm25CorpusPublication(transaction, principal, operation, position),
            OperationKind.OnlineTextPublicationPhase => ExecuteOnlineTextPublication(transaction, principal, operation),
            OperationKind.Batch => ExecuteBatch(transaction, principal, operation, position, placement),
            OperationKind.Receive => Result(Receive(transaction, principal, Payload<ReceiveRequest>(operation), operation.EvaluatedAt, position, operation)),
            OperationKind.Delivery => Result(CompleteDelivery(transaction, principal, Payload<DeliveryCommand>(operation), operation.EvaluatedAt, position, operation)),
            OperationKind.CommitInbox => ExecuteTargetInbox(transaction, principal, operation, position),
            OperationKind.Processing => Result(CompleteProcessing(transaction, principal, Payload<ProcessingRequest>(operation), operation.EvaluatedAt, position)),
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
            _ when BlobStorageOperations.Handles(operation.Kind)
                => new BlobStorageOperations(this).Execute(transaction, principal, operation, position),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, UnsupportedOperationMessage)
        };

    private OperationResult ExecuteBatch(IAtomicTransaction transaction, PrincipalRecord principal, ReplicatedOperation operation,
        long position, AtomicPartitionPlacementResolution? placement)
    {
        var batch = Payload<CommandRequest>(operation);
        if (batch.CommandId != operation.Id)
        {
            throw Errors.Fail(ErrorCode.Validation, BatchEnvelopeMismatchMessage);
        }
        if (placement is null || placement.Partition != batch.Partition)
        {
            throw Errors.Fail(ErrorCode.Corruption, OperationDispatcherBatchPlacementWitnessDoesNotMatchItsTransactionDetail);
        }
        var token = new CommitToken(placement.Incarnation, batch.Partition.AtomicPartitionId, position);
        return Result(new CommitReceipt(batch.CommandId, token,
            ApplyMutations(transaction, principal, batch.Partition, batch.Mutations, operation.EvaluatedAt, position,
                commitToken: token, operation: operation), Durability, AcknowledgedPersistedCopies));
    }

    private static OperationResult ExecuteDispatchState(IAtomicTransaction transaction, ReplicatedOperation operation)
    {
        transaction.PutRecord(KeyCodec.Encode(DispatchSystemSpaceName, DispatchPausedStateName), Payload<bool>(operation));
        return Result(true);
    }
}
