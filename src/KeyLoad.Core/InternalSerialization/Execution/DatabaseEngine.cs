using System.Text.Json;
using KeyLoad.Core.Features.ClusterRouting.Validation;
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
        => CreateNativeOperationCore(kind, id, principalId, evaluatedAt, payload, null);

    internal ReplicatedOperation CreateNativeOperation(OperationKind kind, Guid id, string principalId,
        DateTimeOffset evaluatedAt, ReadOnlyMemory<byte> payload, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        return CreateNativeOperationCore(kind, id, principalId, evaluatedAt, payload, work);
    }

    private ReplicatedOperation CreateNativeOperationCore(OperationKind kind, Guid id, string principalId,
        DateTimeOffset evaluatedAt, ReadOnlyMemory<byte> payload, ReadExecutionBudget? work)
    {
        RequireNativeBudget(payload.Length);
        if (kind == OperationKind.BootstrapPhysicalShardCatalog)
        {
            PhysicalShardCatalogValidation.ValidateEncodedRequestLength(payload.Length);
        }
        if (kind == OperationKind.BindAtomicPartitionPlacement)
        {
            AtomicPartitionPlacementValidation.ValidateEncodedRequestLength(payload.Length);
        }
        if (kind == OperationKind.RegisterPhysicalOwner
            && payload.Length > KeyLoad.Core.Features.ClusterRouting.Contracts.PhysicalOwnerDirectoryProtocol.MaximumRegistrationBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, KeyLoad.Core.Features.ClusterRouting.Contracts.PhysicalOwnerDirectoryProtocol.Capacity);
        }
        payload = payload.ToArray();
        var identity = kind switch
        {
            OperationKind.ConfigurePlacementPolicy => Identity<ConfigurePlacementPolicyRequest>(payload),
            OperationKind.Batch => BatchIdentity(payload, work),
            OperationKind.Receive => Identity<ReceiveRequest>(payload),
            OperationKind.Delivery => Identity<DeliveryCommand>(payload),
            OperationKind.Processing => Identity<ProcessingRequest>(payload),
            OperationKind.CommitInbox => Identity<CommitInboxRequest>(payload),
            OperationKind.ConfigureResource => Identity<ConfigureResourceRequest>(payload),
            OperationKind.CreateTable => Identity<CreateTableRequest>(payload),
            OperationKind.SetQueueDispatch => Identity<QueueDispatchControlRequest>(payload),
            OperationKind.BootstrapPhysicalShardCatalog => Identity<BootstrapPhysicalShardCatalogRequest>(payload),
            OperationKind.BindAtomicPartitionPlacement => Identity<BindAtomicPartitionPlacementRequest>(payload),
            OperationKind.RegisterPhysicalOwner => Identity<RegisterPhysicalOwnerV1>(payload),
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
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, NativeCommandContract.Unsupported)
        };
        var operation = new ReplicatedOperation(id, kind, principalId, evaluatedAt, identity);
        return work is null ? IssueNativeOperation(operation, new NativeCommandPayload(payload))
            : IssueNativeOperation(operation, new NativeCommandPayload(payload), work);
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
