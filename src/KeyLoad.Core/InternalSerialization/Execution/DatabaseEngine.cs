using System.Text.Json;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Features.InternalSerialization;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    /// <summary>Creates the native communication command from typed, generated database input.</summary>
    /// <param name="kind">The exact command capability.</param>
    /// <param name="id">Stable command identity.</param>
    /// <param name="principalId">Persisted principal selected by the caller's trusted boundary.</param>
    /// <param name="evaluatedAt">Original operation evaluation time.</param>
    /// <param name="payload">Native attributed payload for this capability.</param>
    /// <returns>An owned native command retaining the original input fingerprint.</returns>
    public Command<DatabaseCommand> CreateCommand(OperationKind kind, Guid id, string principalId,
        DateTimeOffset evaluatedAt, ReadOnlyMemory<byte> payload)
        => CreateCommandCore(kind, id, principalId, evaluatedAt, payload, null);

    internal Command<DatabaseCommand> CreateCommand(OperationKind kind, Guid id, string principalId,
        DateTimeOffset evaluatedAt, ReadOnlyMemory<byte> payload, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        return CreateCommandCore(kind, id, principalId, evaluatedAt, payload, work);
    }

    private Command<DatabaseCommand> CreateCommand(IKeyValueView admissionView, OperationKind kind, Guid id,
        string principalId, DateTimeOffset evaluatedAt, ReadOnlyMemory<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(admissionView);
        return CreateCommandCore(kind, id, principalId, evaluatedAt, payload, null, admissionView);
    }

    private Command<DatabaseCommand> CreateCommandCore(OperationKind kind, Guid id, string principalId,
        DateTimeOffset evaluatedAt, ReadOnlyMemory<byte> payload, ReadExecutionBudget? work,
        IKeyValueView? admissionView = null)
    {
        RequireNativeBudget(payload.Length);
        payload = payload.ToArray();
        var identity = kind switch
        {
            OperationKind.Batch => BatchIdentity(payload, work),
            OperationKind.Receive => Identity<ReceiveRequest>(payload),
            OperationKind.Delivery => Identity<DeliveryCommand>(payload),
            OperationKind.Processing => Identity<ProcessingRequest>(payload),
            OperationKind.CommitInbox => Identity<CommitInboxRequest>(payload),
            OperationKind.ConfigureResource => Identity<ConfigureResourceRequest>(payload),
            OperationKind.CreateTable => Identity<CreateTableRequest>(payload),
            OperationKind.DropTable => Identity<DropTableRequest>(payload),
            OperationKind.SetQueueDispatch => Identity<QueueDispatchControlRequest>(payload),
            OperationKind.ConfigurePrincipal => Identity<ConfigurePrincipalRequest>(payload),
            OperationKind.ConfigureApiKey => Identity<ConfigureApiKeyRequest>(payload),
            OperationKind.SetDispatch => Identity<bool>(payload),
            OperationKind.RuntimeJournal => Identity<RuntimeJournalMutation>(payload),
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
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, UnsupportedDatabaseCommand)
        };
        return CreatePreparedCommand(kind, id, principalId, evaluatedAt, payload,
            DatabaseCommandFingerprint.Compute(id, kind, principalId, identity), work, admissionView);
    }

    private string BatchIdentity(ReadOnlyMemory<byte> payload, ReadExecutionBudget? work)
    {
        var request = NativeSerialization.Deserialize<CommandRequest>(payload.Span, NativeValidationProfile.PublicInputElements);
        if (!NativeAnalyticsIdentity.HasAnalyticsMutation(request))
        {
            return JsonSerializer.Serialize(request, JsonDefaults.Options);
        }
        if (work is null)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, NativeAnalyticsWorkRequired);
        }
        return NativeAnalyticsIdentity.Canonicalize(request, work, Limits.MaxBatchBytes);
    }

    private const string NativeAnalyticsWorkRequired = "Native analytics issuance requires the original operation budget.";

    private static string Identity<T>(ReadOnlyMemory<byte> payload)
        => JsonSerializer.Serialize(NativeSerialization.Deserialize<T>(payload.Span, NativeValidationProfile.PublicInputElements), JsonDefaults.Options);
}
