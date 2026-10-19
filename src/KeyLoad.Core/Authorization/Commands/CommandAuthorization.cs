using KeyLoad.Core.Features.RuntimeJournaling.Identity;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string ClusterAdministrationRequiredMessage = "Cluster administration is required.";
    private const string MutationCountBudgetMessage = "The mutation count exceeds its budget.";
    private const string UnsupportedBatchMutationMessage = "This mutation is unsupported.";
    private const string CompositionBatchRequiredMessage = "Database composition requires a standalone atomic command.";

    private AtomicPartitionPlacementResolution? AuthorizeOperation(IKeyValueView view, PrincipalRecord principal, Command<DatabaseCommand> operation)
    {
        RuntimeJournalIdentity.RequireOperation(principal, operation.Value!.Kind,
            operation.Value!.Kind == OperationKind.RuntimeJournal ? Payload<RuntimeJournalMutation>(operation) : null);
        if (operation.Value!.Kind == OperationKind.RuntimeJournal)
        {
            return null;
        }
        if (operation.Value!.Kind is OperationKind.ConfigureSubscription or OperationKind.SeekSubscription
            or OperationKind.SetSubscriptionPaused or OperationKind.ReceiveSubscription
            or OperationKind.SubscriptionDelivery or OperationKind.SubscriptionProcessing)
        {
            AuthorizeSubscriptionOperation(view, principal, operation);
            return null;
        }
        return AuthorizeModelOperation(view, principal, operation);
    }

    private AtomicPartitionPlacementResolution? AuthorizeModelOperation(IKeyValueView view,
        PrincipalRecord principal, Command<DatabaseCommand> operation)
    {
        switch (operation.Value!.Kind)
        {
            case OperationKind.Bm25CorpusPublicationPhase:
                AuthorizeBm25CorpusPublication(view, principal, operation);
                return null;
            case OperationKind.FileMetadata:
                AuthorizeFileMetadata(view, principal, Payload<global::KeyLoad.Core.Features.BlobStorage.FileMetadataMutation>(operation));
                return null;
            case OperationKind.Batch:
                return AuthorizeBatch(view, principal, Payload<CommandRequest>(operation));
            case OperationKind.Receive:
                AuthorizeQueueReceiveOperation(view, principal, Payload<ReceiveRequest>(operation));
                break;
            case OperationKind.Delivery:
                var delivery = Payload<DeliveryCommand>(operation);
                Authorization.Require(principal, delivery.Lane.Partition, delivery.Lane.Queue,
                    delivery.Action == DeliveryAction.Renew ? Capability.QueueRenew : Capability.QueueAck);
                break;
            case OperationKind.CommitInbox:
                AuthorizeInbox(view, principal, Payload<CommitInboxRequest>(operation));
                break;
            case OperationKind.Processing:
                var processing = Payload<ProcessingRequest>(operation);
                Authorization.Require(principal, processing.Lane.Partition, processing.Lane.Queue, Capability.QueueAck);
                Authorization.RequireWorkerInput(principal,
                    Resource(view, processing.Lane.Partition, processing.Lane.Queue, ResourceKind.WorkQueue));
                AuthorizeBatch(view, principal, new(processing.CommandId, processing.Lane.Partition, processing.Effects), allowEmpty: true);
                break;
            default:
                if (!principal.ClusterAdministrator)
                {
                    throw Errors.Fail(ErrorCode.PermissionDenied, ClusterAdministrationRequiredMessage);
                }
                break;
        }
        return null;
    }

    private void AuthorizeQueueReceiveOperation(IKeyValueView view, PrincipalRecord principal, ReceiveRequest receive)
    {
        Authorization.Require(principal, receive.Lane.Partition, receive.Lane.Queue, Capability.QueueConsume);
        Authorization.RequireWorkerInput(principal, Resource(view, receive.Lane.Partition, receive.Lane.Queue, ResourceKind.WorkQueue));
    }

    private void AuthorizeSubscriptionOperation(IKeyValueView view, PrincipalRecord principal, Command<DatabaseCommand> operation)
    {
        switch (operation.Value!.Kind)
        {
            case OperationKind.ConfigureSubscription:
                AuthorizeSubscription(view, principal, Payload<ConfigureSubscriptionRequest>(operation).Subscription,
                    Capability.SubscriptionsManage, operation.Timestamp);
                break;
            case OperationKind.SeekSubscription:
                AuthorizeSubscription(view, principal, Payload<SeekSubscriptionRequest>(operation).Subscription,
                    Capability.SubscriptionsManage, operation.Timestamp);
                break;
            case OperationKind.SetSubscriptionPaused:
                AuthorizeSubscription(view, principal, Payload<SetSubscriptionPausedRequest>(operation).Subscription,
                    Capability.SubscriptionsManage, operation.Timestamp);
                break;
            case OperationKind.ReceiveSubscription:
                AuthorizeSubscription(view, principal, Payload<ReceiveSubscriptionRequest>(operation).Subscription,
                    Capability.SubscriptionsConsume, operation.Timestamp, requireDataPrincipal: true);
                break;
            case OperationKind.SubscriptionDelivery:
                AuthorizeSubscription(view, principal, Payload<SubscriptionDeliveryCommand>(operation).Subscription,
                    Capability.SubscriptionsAck, operation.Timestamp, requireDataPrincipal: true);
                break;
            case OperationKind.SubscriptionProcessing:
                var subscriptionProcessing = Payload<SubscriptionProcessingRequest>(operation);
                AuthorizeSubscription(view, principal, subscriptionProcessing.Subscription, Capability.SubscriptionsAck,
                    operation.Timestamp, requireDataPrincipal: true);
                AuthorizeBatch(view, principal, new(subscriptionProcessing.CommandId, subscriptionProcessing.Subscription.Source.Partition, subscriptionProcessing.Effects), allowEmpty: true);
                break;
        }
    }
    private AtomicPartitionPlacementResolution AuthorizeBatch(IKeyValueView view, PrincipalRecord principal, CommandRequest request, bool allowEmpty = false)
    {
        var placement = ReadPlacementWitness(view, request.Partition);
        AuthorizeBatchMutations(view, principal, request, allowEmpty);
        return placement;
    }

    private void AuthorizeExtendedMutation(IKeyValueView view, PrincipalRecord principal, PartitionRef partition, Mutation mutation)
    {
        if (mutation is UpsertAnalyticsEvents or DeleteAnalyticsEvents)
        {
            AuthorizeAnalyticsMutation(view, principal, partition, mutation);
            return;
        }
        if (mutation is InsertTypedRow insert)
        {
            RequireTypedInsertPermissions(principal, partition, BatchResource(view, partition, insert.Collection, ResourceKind.Collection));
            return;
        }
        if (mutation is AdvanceQueueDeadline deadline)
        {
            AuthorizeQueueDeadline(view, principal, partition, deadline);
            return;
        }
        if (mutation is RedriveQueueMessage or CancelQueueMessage or ParkPendingQueueMessage)
        {
            AuthorizeQueueLifecycle(view, principal, partition, mutation);
            return;
        }
        if (mutation is ConfigureRecurringSchedule or EmitRecurringOccurrences or CancelRecurringSchedule or CompareExchangeSaga or ExpireSaga)
        {
            AuthorizeRecurringSagaRequest(view, principal, partition, mutation);
            return;
        }
        if (mutation is ApplyVectorProjection projection)
        {
            ReauthorizeVectorProjection(view, principal, partition, projection);
            return;
        }
        if (mutation is CreateQueueTransfer or AcceptQueueTransfer or CompleteQueueTransfer or AdvanceQueueTransferAttempt or AdvanceQueueTransferRepair)
        {
            AuthorizeQueueTransferRequest(view, principal, partition, mutation);
            return;
        }
        if (mutation is ApplyCrossPartitionReverseEdge or CompleteCrossPartitionReverseEdge)
        {
            ReauthorizeGraphDelivery(view, principal, partition, mutation);
        }
    }

}
