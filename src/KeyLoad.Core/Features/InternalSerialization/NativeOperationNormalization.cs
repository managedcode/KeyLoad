using System.Text.Json;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Features.InternalSerialization;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    /// <summary>Converts public operation JSON once before internal persistence or replication.</summary>
    /// <param name="operation">Trusted envelope retaining exact public identity material.</param>
    /// <returns>The same envelope with native typed payload or a native decode-failure marker.</returns>
    public ReplicatedOperation NormalizeOperation(ReplicatedOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!operation.NativePayload.IsEmpty)
        {
            return VerifyOperationAuthority(operation);
        }
        var payload = operation.Kind switch
        {
            OperationKind.Batch => NormalizePayload<CommandRequest>(operation.PayloadJson),
            OperationKind.Receive => NormalizePayload<ReceiveRequest>(operation.PayloadJson),
            OperationKind.Delivery => NormalizePayload<DeliveryCommand>(operation.PayloadJson),
            OperationKind.Processing => NormalizePayload<ProcessingRequest>(operation.PayloadJson),
            OperationKind.ConfigureResource => NormalizePayload<ConfigureResourceRequest>(operation.PayloadJson),
            OperationKind.ConfigurePrincipal => NormalizePayload<ConfigurePrincipalRequest>(operation.PayloadJson),
            OperationKind.ConfigureApiKey => NormalizePayload<ConfigureApiKeyRequest>(operation.PayloadJson),
            OperationKind.SetDispatch => NormalizePayload<bool>(operation.PayloadJson),
            OperationKind.Membership => NormalizePayload<MembershipMutation>(operation.PayloadJson),
            OperationKind.ConfigureSubscription => NormalizePayload<ConfigureSubscriptionRequest>(operation.PayloadJson),
            OperationKind.SeekSubscription => NormalizePayload<SeekSubscriptionRequest>(operation.PayloadJson),
            OperationKind.ReceiveSubscription => NormalizePayload<ReceiveSubscriptionRequest>(operation.PayloadJson),
            OperationKind.SubscriptionDelivery => NormalizePayload<SubscriptionDeliveryCommand>(operation.PayloadJson),
            OperationKind.SubscriptionProcessing => NormalizePayload<SubscriptionProcessingRequest>(operation.PayloadJson),
            OperationKind.SetSubscriptionPaused => NormalizePayload<SetSubscriptionPausedRequest>(operation.PayloadJson),
            OperationKind.ConfigureProjectionConsumer => NormalizePayload<ConfigureProjectionConsumerRequest>(operation.PayloadJson),
            OperationKind.CommitProjectionBatch => NormalizePayload<CommitProjectionBatchRequest>(operation.PayloadJson),
            OperationKind.ReleaseProjectionConsumer => NormalizePayload<ReleaseProjectionConsumerRequest>(operation.PayloadJson),
            OperationKind.PurgeOutbox => NormalizePayload<PurgeOutboxRequest>(operation.PayloadJson),
            OperationKind.BeginBlobUpload => NormalizePayload<BeginBlobUploadRequest>(operation.PayloadJson),
            OperationKind.WriteBlobPart => NormalizePayload<WriteBlobPartRequest>(operation.PayloadJson),
            OperationKind.CompleteBlobUpload => NormalizePayload<CompleteBlobUploadRequest>(operation.PayloadJson),
            OperationKind.AbortBlobUpload => NormalizePayload<AbortBlobUploadRequest>(operation.PayloadJson),
            OperationKind.DeleteBlob => NormalizePayload<DeleteBlobRequest>(operation.PayloadJson),
            OperationKind.ReclaimBlob => NormalizePayload<ReclaimBlobRequest>(operation.PayloadJson),
            _ => new NativeCommandPayload(ReadOnlyMemory<byte>.Empty, ErrorCode.UnsupportedCapability, NativeCommandContract.Unsupported)
        };
        return IssueNativeOperation(operation, payload);
    }

    private static NativeCommandPayload NormalizePayload<T>(string json)
    {
        T value;
        try
        {
            value = JsonDefaults.Deserialize<T>(json);
        }
        catch (JsonException)
        {
            return new(ReadOnlyMemory<byte>.Empty, ErrorCode.Validation, NativeCommandContract.InvalidJson);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Corruption)
        {
            return new(ReadOnlyMemory<byte>.Empty, error.Code, error.Message);
        }
        return new(NativeSerialization.Serialize(value, NativeValidationProfile.PublicInputElements));
    }
}
