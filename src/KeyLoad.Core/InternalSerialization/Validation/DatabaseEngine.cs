using System.Text;
using System.Text.Json;
using ManagedCode.Communication.Commands;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Features.InternalSerialization;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string UnsupportedDatabaseCommand = "The operation is unsupported.";
    private const string InvalidDatabaseCommandJson = "The operation contains invalid protocol JSON.";

    /// <summary>Parses public JSON once at the trusted boundary and creates an owned native command.</summary>
    /// <param name="kind">The supported database operation.</param>
    /// <param name="id">Original stable command identity.</param>
    /// <param name="principalId">The server-verified subject.</param>
    /// <param name="evaluatedAt">The server-evaluated business time.</param>
    /// <param name="payloadJson">Exact original public input.</param>
    /// <returns>The native communication command containing generated database input.</returns>
    public Command<DatabaseCommand> CreateCommand(OperationKind kind, Guid id, string principalId,
        DateTimeOffset evaluatedAt, string payloadJson)
    {
        ArgumentNullException.ThrowIfNull(payloadJson);
        RequireNativeBudget(Encoding.UTF8.GetByteCount(payloadJson));
        var payload = kind switch
        {
            OperationKind.Batch => NormalizePayload<CommandRequest>(payloadJson),
            OperationKind.Receive => NormalizePayload<ReceiveRequest>(payloadJson),
            OperationKind.Delivery => NormalizePayload<DeliveryCommand>(payloadJson),
            OperationKind.Processing => NormalizePayload<ProcessingRequest>(payloadJson),
            OperationKind.CommitInbox => NormalizePayload<CommitInboxRequest>(payloadJson),
            OperationKind.ConfigureResource => NormalizePayload<ConfigureResourceRequest>(payloadJson),
            OperationKind.CreateTable => NormalizePayload<CreateTableRequest>(payloadJson),
            OperationKind.DropTable => NormalizePayload<DropTableRequest>(payloadJson),
            OperationKind.SetQueueDispatch => NormalizePayload<QueueDispatchControlRequest>(payloadJson),
            OperationKind.ConfigurePrincipal => NormalizePayload<ConfigurePrincipalRequest>(payloadJson),
            OperationKind.ConfigureApiKey => NormalizePayload<ConfigureApiKeyRequest>(payloadJson),
            OperationKind.SetDispatch => NormalizePayload<bool>(payloadJson),
            OperationKind.RuntimeJournal => NormalizePayload<RuntimeJournalMutation>(payloadJson),
            OperationKind.ConfigureSubscription => NormalizePayload<ConfigureSubscriptionRequest>(payloadJson),
            OperationKind.SeekSubscription => NormalizePayload<SeekSubscriptionRequest>(payloadJson),
            OperationKind.ReceiveSubscription => NormalizePayload<ReceiveSubscriptionRequest>(payloadJson),
            OperationKind.SubscriptionDelivery => NormalizePayload<SubscriptionDeliveryCommand>(payloadJson),
            OperationKind.SubscriptionProcessing => NormalizePayload<SubscriptionProcessingRequest>(payloadJson),
            OperationKind.SetSubscriptionPaused => NormalizePayload<SetSubscriptionPausedRequest>(payloadJson),
            OperationKind.ConfigureProjectionConsumer => NormalizePayload<ConfigureProjectionConsumerRequest>(payloadJson),
            OperationKind.CommitProjectionBatch => NormalizePayload<CommitProjectionBatchRequest>(payloadJson),
            OperationKind.ReleaseProjectionConsumer => NormalizePayload<ReleaseProjectionConsumerRequest>(payloadJson),
            OperationKind.PurgeOutbox => NormalizePayload<PurgeOutboxRequest>(payloadJson),
            OperationKind.BeginBlobUpload => NormalizePayload<BeginBlobUploadRequest>(payloadJson),
            OperationKind.WriteBlobPart => NormalizePayload<WriteBlobPartRequest>(payloadJson),
            OperationKind.CompleteBlobUpload => NormalizePayload<CompleteBlobUploadRequest>(payloadJson),
            OperationKind.AbortBlobUpload => NormalizePayload<AbortBlobUploadRequest>(payloadJson),
            OperationKind.DeleteBlob => NormalizePayload<DeleteBlobRequest>(payloadJson),
            OperationKind.ReclaimBlob => NormalizePayload<ReclaimBlobRequest>(payloadJson),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, UnsupportedDatabaseCommand)
        };
        return CreatePreparedCommand(kind, id, principalId, evaluatedAt, payload,
            DatabaseCommandFingerprint.Compute(id, kind, principalId, payloadJson), null);
    }

    private static byte[] NormalizePayload<T>(string json)
    {
        try
        {
            return NativeSerialization.Serialize(JsonDefaults.Deserialize<T>(json), NativeValidationProfile.PublicInputElements);
        }
        catch (JsonException)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidDatabaseCommandJson);
        }
    }
}
