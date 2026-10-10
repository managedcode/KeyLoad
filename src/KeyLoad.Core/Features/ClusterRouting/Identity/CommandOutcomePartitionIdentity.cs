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

        return ResolveKind(operation.Kind, envelope.Value);
    }

    private static CommandOutcomePartitionScope ResolveKind(OperationKind kind, ReadOnlyMemory<byte> payload)
        => kind switch
        {
            OperationKind.OnlineTextPublicationPhase => ForPayload<global::KeyLoad.Core.Features.Search.OnlineTextPublicationPhaseCommand>(payload, static request => request.Request.Consumer.Partition),
            OperationKind.PartitionMovementPhase => ForPayload<PartitionMovePhaseCommand>(payload, static request => request.Partition),
            OperationKind.Batch => ForPayload<CommandRequest>(payload, static request => request.Partition),
            OperationKind.Receive => ForPayload<ReceiveRequest>(payload, static request => request.Lane?.Partition),
            OperationKind.Delivery => ForPayload<DeliveryCommand>(payload, static request => request.Lane?.Partition),
            OperationKind.Processing => ForPayload<ProcessingRequest>(payload, static request => request.Lane?.Partition),
            OperationKind.CommitInbox => ForPayload<CommitInboxRequest>(payload, static request => request.Target?.Partition),
            OperationKind.PurgeOutbox => ForPayload<PurgeOutboxRequest>(payload, static request => request.Partition),
            OperationKind.ConfigureSubscription or OperationKind.SeekSubscription or OperationKind.ReceiveSubscription
                or OperationKind.SubscriptionDelivery or OperationKind.SubscriptionProcessing or OperationKind.SetSubscriptionPaused
                => ResolveSubscription(kind, payload),
            OperationKind.ConfigureProjectionConsumer or OperationKind.CommitProjectionBatch
                or OperationKind.ReleaseProjectionConsumer => ResolveProjection(kind, payload),
            OperationKind.BeginBlobUpload or OperationKind.WriteBlobPart or OperationKind.CompleteBlobUpload
                or OperationKind.AbortBlobUpload or OperationKind.DeleteBlob or OperationKind.ReclaimBlob
                => ResolveBlob(kind, payload),
            OperationKind.ConfigureResource or OperationKind.ConfigurePrincipal or OperationKind.ConfigureApiKey
                or OperationKind.SetDispatch or OperationKind.Membership or OperationKind.BootstrapPhysicalShardCatalog
                or OperationKind.BindAtomicPartitionPlacement or OperationKind.RegisterPhysicalOwner => CommandOutcomePartitionScope.Global,
            _ => CommandOutcomePartitionScope.Unknown
        };

    private static CommandOutcomePartitionScope ResolveSubscription(OperationKind kind, ReadOnlyMemory<byte> payload)
        => kind switch
        {
            OperationKind.ConfigureSubscription => ForPayload<ConfigureSubscriptionRequest>(payload, static request => request.Subscription?.Source?.Partition),
            OperationKind.SeekSubscription => ForPayload<SeekSubscriptionRequest>(payload, static request => request.Subscription?.Source?.Partition),
            OperationKind.ReceiveSubscription => ForPayload<ReceiveSubscriptionRequest>(payload, static request => request.Subscription?.Source?.Partition),
            OperationKind.SubscriptionDelivery => ForPayload<SubscriptionDeliveryCommand>(payload, static request => request.Subscription?.Source?.Partition),
            OperationKind.SubscriptionProcessing => ForPayload<SubscriptionProcessingRequest>(payload, static request => request.Subscription?.Source?.Partition),
            OperationKind.SetSubscriptionPaused => ForPayload<SetSubscriptionPausedRequest>(payload, static request => request.Subscription?.Source?.Partition),
            _ => CommandOutcomePartitionScope.Unknown
        };

    private static CommandOutcomePartitionScope ResolveProjection(OperationKind kind, ReadOnlyMemory<byte> payload)
        => kind switch
        {
            OperationKind.ConfigureProjectionConsumer => ForPayload<ConfigureProjectionConsumerRequest>(payload, static request => request.Consumer?.Partition),
            OperationKind.CommitProjectionBatch => ForPayload<CommitProjectionBatchRequest>(payload, static request => request.Consumer?.Partition),
            OperationKind.ReleaseProjectionConsumer => ForPayload<ReleaseProjectionConsumerRequest>(payload, static request => request.Consumer?.Partition),
            _ => CommandOutcomePartitionScope.Unknown
        };

    private static CommandOutcomePartitionScope ResolveBlob(OperationKind kind, ReadOnlyMemory<byte> payload)
        => kind switch
        {
            OperationKind.BeginBlobUpload => ForPayload<BeginBlobUploadRequest>(payload, static request => request.Blob?.Partition),
            OperationKind.WriteBlobPart => ForPayload<WriteBlobPartRequest>(payload, static request => request.Blob?.Partition),
            OperationKind.CompleteBlobUpload => ForPayload<CompleteBlobUploadRequest>(payload, static request => request.Blob?.Partition),
            OperationKind.AbortBlobUpload => ForPayload<AbortBlobUploadRequest>(payload, static request => request.Blob?.Partition),
            OperationKind.DeleteBlob => ForPayload<DeleteBlobRequest>(payload, static request => request.Blob?.Partition),
            OperationKind.ReclaimBlob => ForPayload<ReclaimBlobRequest>(payload, static request => request.Blob?.Partition),
            _ => CommandOutcomePartitionScope.Unknown
        };

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
