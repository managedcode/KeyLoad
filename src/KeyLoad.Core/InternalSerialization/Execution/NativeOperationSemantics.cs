using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Features.InternalSerialization;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    /// <summary>Compares verified native operations by old envelope identity and typed canonical content.</summary>
    /// <param name="left">One Core-issued operation.</param>
    /// <param name="right">Another Core-issued operation, including an independently owned native copy.</param>
    /// <returns>True only when every original scalar and frozen identity, and typed content, agrees.</returns>
    public bool NativeOperationsEqual(ReplicatedOperation left, ReplicatedOperation right)
    {
        left = VerifyOperationAuthority(left);
        right = VerifyOperationAuthority(right);
        if (left.Id != right.Id || left.Kind != right.Kind || left.PrincipalId != right.PrincipalId
            || left.EvaluatedAt != right.EvaluatedAt || NativeOperationFingerprint.Compute(left) != NativeOperationFingerprint.Compute(right))
        {
            return false;
        }
        var first = NativeSerialization.Deserialize<NativeCommandPayload>(left.NativePayload.Span);
        var second = NativeSerialization.Deserialize<NativeCommandPayload>(right.NativePayload.Span);
        return NativeContentFingerprint(left.Kind, first) == NativeContentFingerprint(right.Kind, second);
    }

    internal static Type? NativeOperationPayloadType(OperationKind kind) => kind switch
    {
        OperationKind.ConfigurePlacementPolicy => typeof(ConfigurePlacementPolicyRequest),
        OperationKind.PlacementIntentPhase => typeof(global::KeyLoad.Core.Features.ClusterRouting.Contracts.PlacementIntentPhaseRequestV1),
        OperationKind.RemoteGraphSource => typeof(global::KeyLoad.Core.Features.GraphTraversal.RemoteGraphSourceMutationV1),
        OperationKind.RemoteGraphReceive => typeof(global::KeyLoad.Core.Features.GraphTraversal.RemoteGraphReceiverMutationV1),
        OperationKind.RemoteGraphComplete => typeof(global::KeyLoad.Core.Features.GraphTraversal.RemoteGraphCompletionMutationV1),
        OperationKind.Bm25CorpusPublicationPhase => typeof(global::KeyLoad.Core.Features.Search.Bm25CorpusPublicationPhaseCommand),
        OperationKind.OnlineTextPublicationPhase => typeof(global::KeyLoad.Core.Features.Search.OnlineTextPublicationPhaseCommand),
        OperationKind.PartitionMovementPhase => typeof(PartitionMovementAuthenticatedCommandV1),
        OperationKind.Batch => typeof(CommandRequest),
        OperationKind.Receive => typeof(ReceiveRequest),
        OperationKind.Delivery => typeof(DeliveryCommand),
        OperationKind.Processing => typeof(ProcessingRequest),
        OperationKind.CommitInbox => typeof(CommitInboxRequest),
        OperationKind.ResourceRetirementPhase => typeof(ResourceRetirementPhaseCommandV1),
        OperationKind.ConfigureResource => typeof(ConfigureResourceRequest),
        OperationKind.CreateTable => typeof(CreateTableRequest),
        OperationKind.SetQueueDispatch => typeof(QueueDispatchControlRequest),
        OperationKind.BootstrapPhysicalShardCatalog => typeof(BootstrapPhysicalShardCatalogRequest),
        OperationKind.BindAtomicPartitionPlacement => typeof(BindAtomicPartitionPlacementRequest),
        OperationKind.RegisterPhysicalOwner => typeof(RegisterPhysicalOwnerV1),
        OperationKind.ConfigurePrincipal => typeof(ConfigurePrincipalRequest),
        OperationKind.ConfigureApiKey => typeof(ConfigureApiKeyRequest),
        OperationKind.SetDispatch => typeof(bool),
        OperationKind.RuntimeJournal => typeof(RuntimeJournalMutation),
        OperationKind.ConfigureSubscription => typeof(ConfigureSubscriptionRequest),
        OperationKind.SeekSubscription => typeof(SeekSubscriptionRequest),
        OperationKind.ReceiveSubscription => typeof(ReceiveSubscriptionRequest),
        OperationKind.SubscriptionDelivery => typeof(SubscriptionDeliveryCommand),
        OperationKind.SubscriptionProcessing => typeof(SubscriptionProcessingRequest),
        OperationKind.SetSubscriptionPaused => typeof(SetSubscriptionPausedRequest),
        OperationKind.ConfigureProjectionConsumer => typeof(ConfigureProjectionConsumerRequest),
        OperationKind.CommitProjectionBatch => typeof(CommitProjectionBatchRequest),
        OperationKind.ReleaseProjectionConsumer => typeof(ReleaseProjectionConsumerRequest),
        OperationKind.PurgeOutbox => typeof(PurgeOutboxRequest),
        OperationKind.BeginBlobUpload => typeof(BeginBlobUploadRequest),
        OperationKind.WriteBlobPart => typeof(WriteBlobPartRequest),
        OperationKind.CompleteBlobUpload => typeof(CompleteBlobUploadRequest),
        OperationKind.AbortBlobUpload => typeof(AbortBlobUploadRequest),
        OperationKind.DeleteBlob => typeof(DeleteBlobRequest),
        OperationKind.ReclaimBlob => typeof(ReclaimBlobRequest),
        _ => null
    };

    private static string NativeContentFingerprint(OperationKind kind, NativeCommandPayload payload)
    {
        if (payload.Error is not null)
        {
            return JsonData.Fingerprint(new
            {
                payload.Error,
                payload.SafeDetail
            });
        }
        return kind switch
        {
            OperationKind.OnlineTextPublicationPhase => NativeTypedFingerprint<global::KeyLoad.Core.Features.Search.OnlineTextPublicationPhaseCommand>(payload.Value),
            OperationKind.PartitionMovementPhase => NativeTypedFingerprint<PartitionMovementAuthenticatedCommandV1>(payload.Value),
            OperationKind.Batch => NativeTypedFingerprint<CommandRequest>(payload.Value),
            OperationKind.Receive => NativeTypedFingerprint<ReceiveRequest>(payload.Value),
            OperationKind.Delivery => NativeTypedFingerprint<DeliveryCommand>(payload.Value),
            OperationKind.Processing => NativeTypedFingerprint<ProcessingRequest>(payload.Value),
            OperationKind.CommitInbox => NativeTypedFingerprint<CommitInboxRequest>(payload.Value),
            OperationKind.ResourceRetirementPhase => NativeTypedFingerprint<ResourceRetirementPhaseCommandV1>(payload.Value),
            OperationKind.ConfigureResource => NativeTypedFingerprint<ConfigureResourceRequest>(payload.Value),
            OperationKind.CreateTable => NativeTypedFingerprint<CreateTableRequest>(payload.Value),
            OperationKind.SetQueueDispatch => NativeTypedFingerprint<QueueDispatchControlRequest>(payload.Value),
            OperationKind.BootstrapPhysicalShardCatalog => NativeTypedFingerprint<BootstrapPhysicalShardCatalogRequest>(payload.Value),
            OperationKind.BindAtomicPartitionPlacement => NativeTypedFingerprint<BindAtomicPartitionPlacementRequest>(payload.Value),
            OperationKind.RegisterPhysicalOwner => NativeTypedFingerprint<RegisterPhysicalOwnerV1>(payload.Value),
            OperationKind.ConfigurePrincipal => NativeTypedFingerprint<ConfigurePrincipalRequest>(payload.Value),
            OperationKind.ConfigureApiKey => NativeTypedFingerprint<ConfigureApiKeyRequest>(payload.Value),
            OperationKind.SetDispatch => NativeTypedFingerprint<bool>(payload.Value),
            OperationKind.RuntimeJournal => NativeTypedFingerprint<RuntimeJournalMutation>(payload.Value),
            OperationKind.ConfigureSubscription => NativeTypedFingerprint<ConfigureSubscriptionRequest>(payload.Value),
            OperationKind.SeekSubscription => NativeTypedFingerprint<SeekSubscriptionRequest>(payload.Value),
            OperationKind.ReceiveSubscription => NativeTypedFingerprint<ReceiveSubscriptionRequest>(payload.Value),
            OperationKind.SubscriptionDelivery => NativeTypedFingerprint<SubscriptionDeliveryCommand>(payload.Value),
            OperationKind.SubscriptionProcessing => NativeTypedFingerprint<SubscriptionProcessingRequest>(payload.Value),
            OperationKind.SetSubscriptionPaused => NativeTypedFingerprint<SetSubscriptionPausedRequest>(payload.Value),
            OperationKind.ConfigureProjectionConsumer => NativeTypedFingerprint<ConfigureProjectionConsumerRequest>(payload.Value),
            OperationKind.CommitProjectionBatch => NativeTypedFingerprint<CommitProjectionBatchRequest>(payload.Value),
            OperationKind.ReleaseProjectionConsumer => NativeTypedFingerprint<ReleaseProjectionConsumerRequest>(payload.Value),
            OperationKind.PurgeOutbox => NativeTypedFingerprint<PurgeOutboxRequest>(payload.Value),
            OperationKind.BeginBlobUpload => NativeTypedFingerprint<BeginBlobUploadRequest>(payload.Value),
            OperationKind.WriteBlobPart => NativeTypedFingerprint<WriteBlobPartRequest>(payload.Value),
            OperationKind.CompleteBlobUpload => NativeTypedFingerprint<CompleteBlobUploadRequest>(payload.Value),
            OperationKind.AbortBlobUpload => NativeTypedFingerprint<AbortBlobUploadRequest>(payload.Value),
            OperationKind.DeleteBlob => NativeTypedFingerprint<DeleteBlobRequest>(payload.Value),
            OperationKind.ReclaimBlob => NativeTypedFingerprint<ReclaimBlobRequest>(payload.Value),
            _ => throw Errors.Fail(ErrorCode.Corruption, NativeCommandContract.MismatchedAuthority)
        };
    }

    internal static string NativeTypedFingerprint<T>(ReadOnlyMemory<byte> value)
        => JsonData.Fingerprint(NativeSerialization.Deserialize<T>(value.Span, NativeValidationProfile.PublicInputElements));
}
