using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Security;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests;

internal static class OutcomeStoreOracle
{
    private const string ScopedOutcomeSpace = "outcome-v2";
    private const string GlobalOutcomeScope = "global";
    private const string UnknownOutcomeScope = "unknown";

    internal static byte[] Key(IAtomicStore store, ReplicatedOperation operation)
    {
        if (!HasNativePayload(store, operation))
        {
            return UnknownKey(operation.PrincipalId, operation.Id);
        }
        var scope = ReadExpectedScope(operation);
        return scope.Partition is { } partition
            ? KeyCodec.Encode(ScopedOutcomeSpace, partition.TenantId, partition.DatabaseId,
                partition.TransactionDomainId, partition.PartitionKey, operation.PrincipalId, operation.Id)
            : scope.IsGlobal
                ? KeyCodec.Encode(ScopedOutcomeSpace, "global", operation.PrincipalId, operation.Id)
                : KeyCodec.Encode(ScopedOutcomeSpace, "unknown", operation.PrincipalId, operation.Id);
    }

    internal static byte[] PartitionKey(PartitionRef partition, string principalId, Guid commandId)
        => KeyCodec.Encode(ScopedOutcomeSpace, partition.TenantId, partition.DatabaseId,
            partition.TransactionDomainId, partition.PartitionKey, principalId, commandId);

    internal static byte[] GlobalKey(string principalId, Guid commandId)
        => KeyCodec.Encode(ScopedOutcomeSpace, GlobalOutcomeScope, principalId, commandId);

    internal static byte[] UnknownKey(string principalId, Guid commandId)
        => KeyCodec.Encode(ScopedOutcomeSpace, UnknownOutcomeScope, principalId, commandId);

    internal static OperationResult? Read(IAtomicStore store, ReplicatedOperation operation)
        => ReadStored(store, operation)?.Result;

    internal static StoredOutcome? ReadStored(IAtomicStore store, ReplicatedOperation operation)
        => store.Read(view => view.GetRecord<StoredOutcome>(Key(store, operation)));

    internal static OperationResult? ReadPartition(IAtomicStore store, PartitionRef partition, string principalId, Guid commandId)
        => store.Read(view => view.GetRecord<StoredOutcome>(PartitionKey(partition, principalId, commandId))?.Result);

    private static (PartitionRef? Partition, bool IsGlobal) ReadExpectedScope(ReplicatedOperation operation)
    {
        try
        {
            var partition = operation.Kind switch
            {
                OperationKind.Batch => Partition<CommandRequest>(operation, static request => request.Partition),
                OperationKind.Receive => Partition<ReceiveRequest>(operation, static request => request.Lane?.Partition),
                OperationKind.Delivery => Partition<DeliveryCommand>(operation, static request => request.Lane?.Partition),
                OperationKind.Processing => Partition<ProcessingRequest>(operation, static request => request.Lane?.Partition),
                OperationKind.PurgeOutbox => Partition<PurgeOutboxRequest>(operation, static request => request.Partition),
                OperationKind.ConfigureSubscription => Partition<ConfigureSubscriptionRequest>(operation, static request => request.Subscription?.Source?.Partition),
                OperationKind.SeekSubscription => Partition<SeekSubscriptionRequest>(operation, static request => request.Subscription?.Source?.Partition),
                OperationKind.ReceiveSubscription => Partition<ReceiveSubscriptionRequest>(operation, static request => request.Subscription?.Source?.Partition),
                OperationKind.SubscriptionDelivery => Partition<SubscriptionDeliveryCommand>(operation, static request => request.Subscription?.Source?.Partition),
                OperationKind.SubscriptionProcessing => Partition<SubscriptionProcessingRequest>(operation, static request => request.Subscription?.Source?.Partition),
                OperationKind.SetSubscriptionPaused => Partition<SetSubscriptionPausedRequest>(operation, static request => request.Subscription?.Source?.Partition),
                OperationKind.ConfigureProjectionConsumer => Partition<ConfigureProjectionConsumerRequest>(operation, static request => request.Consumer?.Partition),
                OperationKind.CommitProjectionBatch => Partition<CommitProjectionBatchRequest>(operation, static request => request.Consumer?.Partition),
                OperationKind.ReleaseProjectionConsumer => Partition<ReleaseProjectionConsumerRequest>(operation, static request => request.Consumer?.Partition),
                OperationKind.BeginBlobUpload => Partition<BeginBlobUploadRequest>(operation, static request => request.Blob?.Partition),
                OperationKind.WriteBlobPart => Partition<WriteBlobPartRequest>(operation, static request => request.Blob?.Partition),
                OperationKind.CompleteBlobUpload => Partition<CompleteBlobUploadRequest>(operation, static request => request.Blob?.Partition),
                OperationKind.AbortBlobUpload => Partition<AbortBlobUploadRequest>(operation, static request => request.Blob?.Partition),
                OperationKind.DeleteBlob => Partition<DeleteBlobRequest>(operation, static request => request.Blob?.Partition),
                OperationKind.ReclaimBlob => Partition<ReclaimBlobRequest>(operation, static request => request.Blob?.Partition),
                _ => null
            };
            if (Valid(partition))
            { return (partition, false); }
            return (null, IsGlobalOperation(operation));
        }
        catch (JsonException)
        {
            return (null, false);
        }
        catch (KeyLoadException)
        {
            return (null, false);
        }
    }

    private static bool IsGlobalOperation(ReplicatedOperation operation)
        => operation.Kind switch
        {
            OperationKind.ConfigureResource => IsNonNull<ConfigureResourceRequest>(operation),
            OperationKind.ConfigurePrincipal => IsNonNull<ConfigurePrincipalRequest>(operation),
            OperationKind.ConfigureApiKey => IsNonNull<ConfigureApiKeyRequest>(operation),
            OperationKind.BootstrapPhysicalShardCatalog => IsNonNull<BootstrapPhysicalShardCatalogRequest>(operation),
            OperationKind.BindAtomicPartitionPlacement => IsNonNull<BindAtomicPartitionPlacementRequest>(operation),
            OperationKind.Membership => IsNonNull<MembershipMutation>(operation),
            OperationKind.SetDispatch => IsBoolean(operation.PayloadJson),
            _ => false
        };

    private static bool HasNativePayload(IAtomicStore store, ReplicatedOperation operation)
    {
        try
        {
            var normalized = new DatabaseEngine(store, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.TimeSeriesExecution()).NormalizeOperation(operation);
            var payload = NativeSerialization.Deserialize<NativeCommandPayload>(normalized.NativePayload.Span);
            return payload.Error is null && !payload.Value.IsEmpty;
        }
        catch (KeyLoadException)
        {
            return false;
        }
    }

    private static bool IsBoolean(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind is JsonValueKind.True or JsonValueKind.False;
    }

    private static PartitionRef? Partition<T>(ReplicatedOperation operation, Func<T, PartitionRef?> select)
        where T : class
    {
        var request = JsonSerializer.Deserialize<T>(operation.PayloadJson, JsonDefaults.Options);
        return request is null ? null : select(request);
    }

    private static bool IsNonNull<T>(ReplicatedOperation operation) where T : class
        => JsonSerializer.Deserialize<T>(operation.PayloadJson, JsonDefaults.Options) is not null;

    private static bool Valid(PartitionRef? partition)
        => partition is not null && !string.IsNullOrWhiteSpace(partition.TenantId)
            && !string.IsNullOrWhiteSpace(partition.DatabaseId)
            && !string.IsNullOrWhiteSpace(partition.TransactionDomainId)
            && !string.IsNullOrWhiteSpace(partition.PartitionKey);
}
