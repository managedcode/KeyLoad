using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string ClusterAdministrationRequiredMessage = "Cluster administration is required.";
    private const string StalePartitionOwnershipMessage = "The partition ownership epoch is stale.";
    private const string MutationCountBudgetMessage = "The mutation count exceeds its budget.";
    private const string UnsupportedBatchMutationMessage = "This mutation is unsupported.";
    private const string CompositionBatchRequiredMessage = "Database composition requires a standalone atomic command.";

    private AtomicPartitionPlacementResolution? AuthorizeOperation(IKeyValueView view, PrincipalRecord principal, ReplicatedOperation operation)
    {
        ClusterPrincipalPolicy.RequireOperation(principal, operation.Kind);
        RuntimeJournalIdentity.RequireOperation(principal, operation.Kind,
            operation.Kind == OperationKind.RuntimeJournal ? Payload<RuntimeJournalMutation>(operation) : null);
        if (operation.Kind == OperationKind.RuntimeJournal)
        {
            return null;
        }
        if (BlobStorageOperations.Handles(operation.Kind))
        {
            new BlobStorageOperations(this).Authorize(view, principal, operation);
            return null;
        }
        if (operation.Kind is OperationKind.ConfigureSubscription or OperationKind.SeekSubscription
            or OperationKind.SetSubscriptionPaused or OperationKind.ReceiveSubscription
            or OperationKind.SubscriptionDelivery or OperationKind.SubscriptionProcessing)
        {
            AuthorizeSubscriptionOperation(view, principal, operation);
            return null;
        }
        switch (operation.Kind)
        {
            case OperationKind.Batch:
                return AuthorizeBatch(view, principal, Payload<CommandRequest>(operation));
            case OperationKind.Receive:
                var receive = Payload<ReceiveRequest>(operation);
                Authorization.Require(principal, receive.Lane.Partition, receive.Lane.Queue, Capability.QueueConsume);
                Authorization.RequireWorkerInput(principal, Resource(view, receive.Lane.Partition, receive.Lane.Queue, ResourceKind.WorkQueue));
                break;
            case OperationKind.Delivery:
                var delivery = Payload<DeliveryCommand>(operation);
                Authorization.Require(principal, delivery.Lane.Partition, delivery.Lane.Queue,
                    delivery.Action == DeliveryAction.Renew ? Capability.QueueRenew : Capability.QueueAck);
                break;
            case OperationKind.Processing:
                var processing = Payload<ProcessingRequest>(operation);
                Authorization.Require(principal, processing.Lane.Partition, processing.Lane.Queue, Capability.QueueAck);
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

    private void AuthorizeSubscriptionOperation(IKeyValueView view, PrincipalRecord principal, ReplicatedOperation operation)
    {
        switch (operation.Kind)
        {
            case OperationKind.ConfigureSubscription:
                AuthorizeSubscription(view, principal, Payload<ConfigureSubscriptionRequest>(operation).Subscription,
                    Capability.SubscriptionsManage, operation.EvaluatedAt);
                break;
            case OperationKind.SeekSubscription:
                AuthorizeSubscription(view, principal, Payload<SeekSubscriptionRequest>(operation).Subscription,
                    Capability.SubscriptionsManage, operation.EvaluatedAt);
                break;
            case OperationKind.SetSubscriptionPaused:
                AuthorizeSubscription(view, principal, Payload<SetSubscriptionPausedRequest>(operation).Subscription,
                    Capability.SubscriptionsManage, operation.EvaluatedAt);
                break;
            case OperationKind.ReceiveSubscription:
                AuthorizeSubscription(view, principal, Payload<ReceiveSubscriptionRequest>(operation).Subscription,
                    Capability.SubscriptionsConsume, operation.EvaluatedAt, requireDataPrincipal: true);
                break;
            case OperationKind.SubscriptionDelivery:
                AuthorizeSubscription(view, principal, Payload<SubscriptionDeliveryCommand>(operation).Subscription,
                    Capability.SubscriptionsAck, operation.EvaluatedAt, requireDataPrincipal: true);
                break;
            case OperationKind.SubscriptionProcessing:
                var subscriptionProcessing = Payload<SubscriptionProcessingRequest>(operation);
                AuthorizeSubscription(view, principal, subscriptionProcessing.Subscription, Capability.SubscriptionsAck,
                    operation.EvaluatedAt, requireDataPrincipal: true);
                AuthorizeBatch(view, principal, new(subscriptionProcessing.CommandId, subscriptionProcessing.Subscription.Source.Partition, subscriptionProcessing.Effects), allowEmpty: true);
                break;
        }
    }
    private AtomicPartitionPlacementResolution AuthorizeBatch(IKeyValueView view, PrincipalRecord principal, CommandRequest request, bool allowEmpty = false)
    {
        var placement = ReadPlacementWitness(view, request.Partition);
        if (request.OwnershipEpoch != placement.PlacementEpoch)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, StalePartitionOwnershipMessage);
        }

        AuthorizeBatchMutations(view, principal, request, allowEmpty, BatchResourceAdmission.CurrentPhysicalOwner);
        return placement;
    }

    private void AuthorizeExtendedMutation(IKeyValueView view, PrincipalRecord principal, PartitionRef partition, Mutation mutation)
    {
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
        if (mutation is CreateQueueTransfer or AcceptQueueTransfer or CompleteQueueTransfer)
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
