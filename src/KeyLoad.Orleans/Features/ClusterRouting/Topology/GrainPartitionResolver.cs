using KeyLoad.Core;

namespace KeyLoad.Orleans;

internal static class GrainPartitionResolver
{
    internal static string Resolve(DecodedGrainRequest request) => request.Envelope.CommandKind switch
    {
        OperationKind.Batch => Route<CommandRequest>(request, value => (value.CommandId, value.Partition)),
        OperationKind.Receive => Route<ReceiveRequest>(request, value => (value.RequestId, value.Lane.Partition)),
        OperationKind.Delivery => Route<DeliveryCommand>(request, value => (value.CommandId, value.Lane.Partition)),
        OperationKind.Processing => Route<ProcessingRequest>(request, value => (value.CommandId, value.Lane.Partition)),
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
        OperationKind.BeginBlobUpload => Route<BeginBlobUploadRequest>(request, value => (value.CommandId, value.Blob.Partition)),
        OperationKind.WriteBlobPart => Route<WriteBlobPartRequest>(request, value => (value.CommandId, value.Blob.Partition)),
        OperationKind.CompleteBlobUpload => Route<CompleteBlobUploadRequest>(request, value => (value.CommandId, value.Blob.Partition)),
        OperationKind.AbortBlobUpload => Route<AbortBlobUploadRequest>(request, value => (value.CommandId, value.Blob.Partition)),
        OperationKind.DeleteBlob => Route<DeleteBlobRequest>(request, value => (value.CommandId, value.Blob.Partition)),
        OperationKind.ReclaimBlob => Route<ReclaimBlobRequest>(request, value => (value.CommandId, value.Blob.Partition)),
        OperationKind.ConfigureResource or OperationKind.SetDispatch => GrainRoutingProtocol.CatalogPartition,
        OperationKind.ConfigurePrincipal or OperationKind.ConfigureApiKey => GrainRoutingProtocol.AuthorizationPartition,
        OperationKind.Membership => throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired),
        _ => throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest)
    };

    private static string Route<T>(DecodedGrainRequest request, Func<T, (Guid CommandId, PartitionRef Partition)> identity)
    {
        var command = GrainNativePayload.ReadCommand<T>(request.Payload);
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
