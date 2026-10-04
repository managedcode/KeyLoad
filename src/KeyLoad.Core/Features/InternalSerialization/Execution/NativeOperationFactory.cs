using System.Text.Json;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Features.InternalSerialization;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    /// <summary>Creates an internal native command and the frozen JSON identity material used by deduplication.</summary>
    /// <param name="kind">The exact command capability.</param>
    /// <param name="id">Stable command identity.</param>
    /// <param name="principalId">Persisted principal selected by the caller's trusted boundary.</param>
    /// <param name="evaluatedAt">Leader evaluation time.</param>
    /// <param name="payload">Native attributed payload for this capability.</param>
    /// <returns>The typed internal operation; public JSON is retained solely for its existing fingerprint.</returns>
    public ReplicatedOperation CreateNativeOperation(OperationKind kind, Guid id, string principalId,
        DateTimeOffset evaluatedAt, ReadOnlyMemory<byte> payload)
    {
        RequireNativeBudget(payload.Length);
        payload = payload.ToArray();
        var identity = kind switch
        {
            OperationKind.Batch => Identity<CommandRequest>(payload),
            OperationKind.Receive => Identity<ReceiveRequest>(payload),
            OperationKind.Delivery => Identity<DeliveryCommand>(payload),
            OperationKind.Processing => Identity<ProcessingRequest>(payload),
            OperationKind.ConfigureResource => Identity<ConfigureResourceRequest>(payload),
            OperationKind.ConfigurePrincipal => Identity<ConfigurePrincipalRequest>(payload),
            OperationKind.ConfigureApiKey => Identity<ConfigureApiKeyRequest>(payload),
            OperationKind.SetDispatch => Identity<bool>(payload),
            OperationKind.Membership => Identity<MembershipMutation>(payload),
            OperationKind.ConfigureSubscription => Identity<ConfigureSubscriptionRequest>(payload),
            OperationKind.SeekSubscription => Identity<SeekSubscriptionRequest>(payload),
            OperationKind.ReceiveSubscription => Identity<ReceiveSubscriptionRequest>(payload),
            OperationKind.SubscriptionDelivery => Identity<SubscriptionDeliveryCommand>(payload),
            OperationKind.SubscriptionProcessing => Identity<SubscriptionProcessingRequest>(payload),
            OperationKind.SetSubscriptionPaused => Identity<SetSubscriptionPausedRequest>(payload),
            OperationKind.ConfigureProjectionConsumer => Identity<ConfigureProjectionConsumerRequest>(payload),
            OperationKind.CommitProjectionBatch => Identity<CommitProjectionBatchRequest>(payload),
            OperationKind.ReleaseProjectionConsumer => Identity<ReleaseProjectionConsumerRequest>(payload),
            OperationKind.PurgeOutbox => Identity<PurgeOutboxRequest>(payload),
            OperationKind.BeginBlobUpload => Identity<BeginBlobUploadRequest>(payload),
            OperationKind.WriteBlobPart => Identity<WriteBlobPartRequest>(payload),
            OperationKind.CompleteBlobUpload => Identity<CompleteBlobUploadRequest>(payload),
            OperationKind.AbortBlobUpload => Identity<AbortBlobUploadRequest>(payload),
            OperationKind.DeleteBlob => Identity<DeleteBlobRequest>(payload),
            OperationKind.ReclaimBlob => Identity<ReclaimBlobRequest>(payload),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, NativeCommandContract.Unsupported)
        };
        return IssueNativeOperation(new(id, kind, principalId, evaluatedAt, identity), new NativeCommandPayload(payload));
    }

    private static string Identity<T>(ReadOnlyMemory<byte> payload)
        => JsonSerializer.Serialize(NativeSerialization.Deserialize<T>(payload.Span, NativeValidationProfile.PublicInputElements), JsonDefaults.Options);
}
