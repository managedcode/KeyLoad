using KeyLoad.Core;

namespace KeyLoad.Clustering;

internal static class GrainPartitionResolver
{
    internal static string Resolve(DecodedGrainRequest request) => request.Envelope.CommandKind switch
    {
        OperationKind.OnlineTextPublicationPhase => GrainOnlineTextPublicationCommand.Resolve(request),
        OperationKind.Bm25CorpusPublicationPhase => GrainBm25CorpusPublicationCommand.Resolve(request),
        OperationKind.FileMetadata => Route<global::KeyLoad.Core.BlobStorage.FileMetadataMutation>(request, value => (value.CommandId, value.Address.Partition)),
        OperationKind.Batch => Route<CommandRequest>(request, value => (value.CommandId, value.Partition)),
        OperationKind.Receive => Route<ReceiveRequest>(request, value => (value.RequestId, value.Lane.Partition)),
        OperationKind.Delivery => Route<DeliveryCommand>(request, value => (value.CommandId, value.Lane.Partition)),
        OperationKind.Processing => Route<ProcessingRequest>(request, value => (value.CommandId, value.Lane.Partition)),
        OperationKind.CommitInbox => Route<CommitInboxRequest>(request, value => (value.CommandId, value.Target.Partition)),
        OperationKind.ConfigureSubscription => Route<ConfigureSubscriptionRequest>(request, value => (value.CommandId, value.Subscription.Source.Partition)),
        OperationKind.SeekSubscription => Route<SeekSubscriptionRequest>(request, value => (value.CommandId, value.Subscription.Source.Partition)),
        OperationKind.ReceiveSubscription => Route<ReceiveSubscriptionRequest>(request, value => (value.RequestId, value.Subscription.Source.Partition)),
        OperationKind.SubscriptionDelivery => Route<SubscriptionDeliveryCommand>(request, value => (value.CommandId, value.Subscription.Source.Partition)),
        OperationKind.SubscriptionProcessing => Route<SubscriptionProcessingRequest>(request, value => (value.CommandId, value.Subscription.Source.Partition)),
        OperationKind.SetSubscriptionPaused => Route<SetSubscriptionPausedRequest>(request, value => (value.CommandId, value.Subscription.Source.Partition)),
        OperationKind.ConfigureProjectionConsumer => Route<ConfigureProjectionConsumerRequest>(request, value => (value.CommandId, value.Consumer.Partition)),
        OperationKind.CommitProjectionBatch => Route<CommitProjectionBatchRequest>(request, value => (value.CommandId, value.Consumer.Partition)),
        OperationKind.ReleaseProjectionConsumer => Route<ReleaseProjectionConsumerRequest>(request, value => (value.CommandId, value.Consumer.Partition)),
        OperationKind.PurgeOutbox => Route<PurgeOutboxRequest>(request, value => (value.CommandId, value.Partition)),
        OperationKind.ConfigureResource or OperationKind.CreateTable or OperationKind.DropTable or OperationKind.SetDispatch
            or OperationKind.SetQueueDispatch or OperationKind.RuntimeJournal
            => GrainRoutingProtocol.CatalogPartition,
        OperationKind.ConfigurePrincipal or OperationKind.ConfigureApiKey => GrainRoutingProtocol.AuthorizationPartition,
        _ => throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest)
    };

    private static string Route<T>(DecodedGrainRequest request, Func<T, (Guid CommandId, PartitionRef Partition)> identity)
    {
        var command = GrainPayload.ReadCommand<T>(request.Payload);
        var (commandId, partition) = identity(command);
        if (commandId != request.Envelope.CommandId)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }

        ArgumentNullException.ThrowIfNull(partition);
        JsonData.Identifier(partition.TenantId);
        JsonData.Identifier(partition.DatabaseId);
        JsonData.Identifier(partition.TransactionDomainId);
        JsonData.Identifier(partition.PartitionKey);
        return partition.AtomicPartitionId;
    }
}
