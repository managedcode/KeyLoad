using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Features.InternalSerialization;

namespace KeyLoad.Core.Features.ClusterRouting.Identity;

internal static class CommandOutcomePartitionIdentity
{
    internal static CommandOutcomePartitionScope Resolve(ReplicatedOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.NativePayload.IsEmpty)
        {
            return CommandOutcomePartitionScope.Unknown;
        }

        var envelope = NativeSerialization.Deserialize<NativeCommandPayload>(operation.NativePayload.Span);
        if (envelope.Error is not null || envelope.Value.IsEmpty)
        {
            return CommandOutcomePartitionScope.Unknown;
        }

        return operation.Kind switch
        {
            OperationKind.Batch => ForPayload<CommandRequest>(envelope.Value, static request => request.Partition),
            OperationKind.Receive => ForPayload<ReceiveRequest>(envelope.Value, static request => request.Lane?.Partition),
            OperationKind.Delivery => ForPayload<DeliveryCommand>(envelope.Value, static request => request.Lane?.Partition),
            OperationKind.Processing => ForPayload<ProcessingRequest>(envelope.Value, static request => request.Lane?.Partition),
            OperationKind.ConfigureSubscription => ForPayload<ConfigureSubscriptionRequest>(envelope.Value, static request => request.Subscription?.Source?.Partition),
            OperationKind.SeekSubscription => ForPayload<SeekSubscriptionRequest>(envelope.Value, static request => request.Subscription?.Source?.Partition),
            OperationKind.ReceiveSubscription => ForPayload<ReceiveSubscriptionRequest>(envelope.Value, static request => request.Subscription?.Source?.Partition),
            OperationKind.SubscriptionDelivery => ForPayload<SubscriptionDeliveryCommand>(envelope.Value, static request => request.Subscription?.Source?.Partition),
            OperationKind.SubscriptionProcessing => ForPayload<SubscriptionProcessingRequest>(envelope.Value, static request => request.Subscription?.Source?.Partition),
            OperationKind.SetSubscriptionPaused => ForPayload<SetSubscriptionPausedRequest>(envelope.Value, static request => request.Subscription?.Source?.Partition),
            OperationKind.ConfigureProjectionConsumer => ForPayload<ConfigureProjectionConsumerRequest>(envelope.Value, static request => request.Consumer?.Partition),
            OperationKind.CommitProjectionBatch => ForPayload<CommitProjectionBatchRequest>(envelope.Value, static request => request.Consumer?.Partition),
            OperationKind.ReleaseProjectionConsumer => ForPayload<ReleaseProjectionConsumerRequest>(envelope.Value, static request => request.Consumer?.Partition),
            OperationKind.PurgeOutbox => ForPayload<PurgeOutboxRequest>(envelope.Value, static request => request.Partition),
            OperationKind.BeginBlobUpload => ForPayload<BeginBlobUploadRequest>(envelope.Value, static request => request.Blob?.Partition),
            OperationKind.WriteBlobPart => ForPayload<WriteBlobPartRequest>(envelope.Value, static request => request.Blob?.Partition),
            OperationKind.CompleteBlobUpload => ForPayload<CompleteBlobUploadRequest>(envelope.Value, static request => request.Blob?.Partition),
            OperationKind.AbortBlobUpload => ForPayload<AbortBlobUploadRequest>(envelope.Value, static request => request.Blob?.Partition),
            OperationKind.DeleteBlob => ForPayload<DeleteBlobRequest>(envelope.Value, static request => request.Blob?.Partition),
            OperationKind.ReclaimBlob => ForPayload<ReclaimBlobRequest>(envelope.Value, static request => request.Blob?.Partition),
            OperationKind.ConfigureResource or OperationKind.ConfigurePrincipal or OperationKind.ConfigureApiKey
                or OperationKind.SetDispatch or OperationKind.Membership or OperationKind.BootstrapPhysicalShardCatalog
                or OperationKind.BindAtomicPartitionPlacement => CommandOutcomePartitionScope.Global,
            _ => CommandOutcomePartitionScope.Unknown
        };
    }

    private static CommandOutcomePartitionScope ForPayload<T>(ReadOnlyMemory<byte> payload,
        Func<T, PartitionRef?> partition) where T : class
    {
        var request = NativeSerialization.Deserialize<T>(payload.Span, NativeValidationProfile.PublicInputElements);
        if (request is null)
        {
            return CommandOutcomePartitionScope.Unknown;
        }

        var value = partition(request);
        if (value is null)
        {
            return CommandOutcomePartitionScope.Unknown;
        }

        try
        {
            DatabaseEngine.ValidatePartition(value);
            return new(CommandOutcomeScopeKind.Partition, value);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Validation)
        {
            return CommandOutcomePartitionScope.Unknown;
        }
    }
}
