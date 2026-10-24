using KeyLoad.Core.RuntimeJournaling.Identity;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes CommandAuthorization operations against the caller's database context.</summary>
public static class CommandAuthorization
{
    internal const string ClusterAdministrationRequiredMessage = "Cluster administration is required.";
    internal const string MutationCountBudgetMessage = "The mutation count exceeds its budget.";
    internal const string UnsupportedBatchMutationMessage = "This mutation is unsupported.";
    internal const string CompositionBatchRequiredMessage = "Database composition requires a standalone atomic command.";
    internal static AtomicPartitionPlacementResolution? AuthorizeOperation(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, Command<DatabaseCommand> operation)
    {
        RuntimeJournalIdentity.RequireOperation(principal, operation.Value!.Kind, operation.Value!.Kind == OperationKind.RuntimeJournal ? OperationProtocol.Payload<RuntimeJournalMutation>(operation) : null);
        if (operation.Value!.Kind == OperationKind.RuntimeJournal)
        {
            return null;
        }

        if (operation.Value!.Kind is OperationKind.ConfigureSubscription or OperationKind.SeekSubscription or OperationKind.SetSubscriptionPaused or OperationKind.ReceiveSubscription or OperationKind.SubscriptionDelivery or OperationKind.SubscriptionProcessing)
        {
            engine.AuthorizeSubscriptionOperation(view, principal, operation);
            return null;
        }

        return engine.AuthorizeModelOperation(view, principal, operation);
    }

    internal static AtomicPartitionPlacementResolution? AuthorizeModelOperation(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, Command<DatabaseCommand> operation)
    {
        switch (operation.Value!.Kind)
        {
            case OperationKind.Bm25CorpusPublicationPhase:
                engine.AuthorizeBm25CorpusPublication(view, principal, operation);
                return null;
            case OperationKind.FileMetadata:
                engine.AuthorizeFileMetadata(view, principal, OperationProtocol.Payload<global::KeyLoad.Core.BlobStorage.FileMetadataMutation>(operation));
                return null;
            case OperationKind.Batch:
                return engine.AuthorizeBatch(view, principal, OperationProtocol.Payload<CommandRequest>(operation));
            case OperationKind.Receive:
                engine.AuthorizeQueueReceiveOperation(view, principal, OperationProtocol.Payload<ReceiveRequest>(operation));
                break;
            case OperationKind.Delivery:
                var delivery = OperationProtocol.Payload<DeliveryCommand>(operation);
                engine.Authorization.Require(principal, delivery.Lane.Partition, delivery.Lane.Queue, delivery.Action == DeliveryAction.Renew ? Capability.QueueRenew : Capability.QueueAck);
                break;
            case OperationKind.CommitInbox:
                engine.AuthorizeInbox(view, principal, OperationProtocol.Payload<CommitInboxRequest>(operation));
                break;
            case OperationKind.Processing:
                var processing = OperationProtocol.Payload<ProcessingRequest>(operation);
                engine.Authorization.Require(principal, processing.Lane.Partition, processing.Lane.Queue, Capability.QueueAck);
                engine.Authorization.RequireWorkerInput(principal, engine.Resource(view, processing.Lane.Partition, processing.Lane.Queue, ResourceKind.WorkQueue));
                engine.AuthorizeBatch(view, principal, new(processing.CommandId, processing.Lane.Partition, processing.Effects), allowEmpty: true);
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

    internal static void AuthorizeQueueReceiveOperation(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, ReceiveRequest receive)
    {
        engine.Authorization.Require(principal, receive.Lane.Partition, receive.Lane.Queue, Capability.QueueConsume);
        engine.Authorization.RequireWorkerInput(principal, engine.Resource(view, receive.Lane.Partition, receive.Lane.Queue, ResourceKind.WorkQueue));
    }

    internal static void AuthorizeSubscriptionOperation(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, Command<DatabaseCommand> operation)
    {
        switch (operation.Value!.Kind)
        {
            case OperationKind.ConfigureSubscription:
                engine.AuthorizeSubscription(view, principal, OperationProtocol.Payload<ConfigureSubscriptionRequest>(operation).Subscription, Capability.SubscriptionsManage, operation.Timestamp);
                break;
            case OperationKind.SeekSubscription:
                engine.AuthorizeSubscription(view, principal, OperationProtocol.Payload<SeekSubscriptionRequest>(operation).Subscription, Capability.SubscriptionsManage, operation.Timestamp);
                break;
            case OperationKind.SetSubscriptionPaused:
                engine.AuthorizeSubscription(view, principal, OperationProtocol.Payload<SetSubscriptionPausedRequest>(operation).Subscription, Capability.SubscriptionsManage, operation.Timestamp);
                break;
            case OperationKind.ReceiveSubscription:
                engine.AuthorizeSubscription(view, principal, OperationProtocol.Payload<ReceiveSubscriptionRequest>(operation).Subscription, Capability.SubscriptionsConsume, operation.Timestamp, requireDataPrincipal: true);
                break;
            case OperationKind.SubscriptionDelivery:
                engine.AuthorizeSubscription(view, principal, OperationProtocol.Payload<SubscriptionDeliveryCommand>(operation).Subscription, Capability.SubscriptionsAck, operation.Timestamp, requireDataPrincipal: true);
                break;
            case OperationKind.SubscriptionProcessing:
                var subscriptionProcessing = OperationProtocol.Payload<SubscriptionProcessingRequest>(operation);
                engine.AuthorizeSubscription(view, principal, subscriptionProcessing.Subscription, Capability.SubscriptionsAck, operation.Timestamp, requireDataPrincipal: true);
                engine.AuthorizeBatch(view, principal, new(subscriptionProcessing.CommandId, subscriptionProcessing.Subscription.Source.Partition, subscriptionProcessing.Effects), allowEmpty: true);
                break;
        }
    }

    internal static AtomicPartitionPlacementResolution AuthorizeBatch(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, CommandRequest request, bool allowEmpty = false)
    {
        var placement = engine.ReadPlacementWitness(view, request.Partition);
        engine.AuthorizeBatchMutations(view, principal, request, allowEmpty);
        return placement;
    }

    internal static void AuthorizeExtendedMutation(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, Mutation mutation)
    {
        if (mutation is UpsertAnalyticsEvents or DeleteAnalyticsEvents)
        {
            engine.AuthorizeAnalyticsMutation(view, principal, partition, mutation);
            return;
        }

        if (mutation is InsertTypedRow insert)
        {
            engine.RequireTypedInsertPermissions(principal, partition, engine.BatchResource(view, partition, insert.Collection, ResourceKind.Collection));
            return;
        }

        if (mutation is AdvanceQueueDeadline deadline)
        {
            engine.AuthorizeQueueDeadline(view, principal, partition, deadline);
            return;
        }

        if (mutation is RedriveQueueMessage or CancelQueueMessage or ParkPendingQueueMessage)
        {
            engine.AuthorizeQueueLifecycle(view, principal, partition, mutation);
            return;
        }

        if (mutation is ConfigureRecurringSchedule or EmitRecurringOccurrences or CancelRecurringSchedule or CompareExchangeSaga or ExpireSaga)
        {
            engine.AuthorizeRecurringSagaRequest(view, principal, partition, mutation);
            return;
        }

        if (mutation is ApplyVectorProjection projection)
        {
            engine.ReauthorizeVectorProjection(view, principal, partition, projection);
            return;
        }

        if (mutation is CreateQueueTransfer or AcceptQueueTransfer or CompleteQueueTransfer or AdvanceQueueTransferAttempt or AdvanceQueueTransferRepair)
        {
            engine.AuthorizeQueueTransferRequest(view, principal, partition, mutation);
            return;
        }

        if (mutation is ApplyCrossPartitionReverseEdge or CompleteCrossPartitionReverseEdge)
        {
            engine.ReauthorizeGraphDelivery(view, principal, partition, mutation);
        }
    }
}
