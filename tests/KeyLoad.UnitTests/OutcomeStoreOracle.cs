using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests;

internal static class OutcomeStoreOracle
{
    private const string ScopedOutcomeSpace = "outcome-v2";
    private const string GlobalOutcomeScope = "global";
    private const string UnknownOutcomeScope = "unknown";

    internal static byte[] Key(Command<DatabaseCommand> operation)
    {
        if (operation.Value is null || operation.Value.Input.IsEmpty)
        {
            return UnknownKey(operation.UserId!, operation.CommandId);
        }
        var scope = ReadExpectedScope(operation);
        return scope.Partition is { } partition
            ? KeyCodec.Encode(ScopedOutcomeSpace, partition.TenantId, partition.DatabaseId,
                partition.TransactionDomainId, partition.PartitionKey, operation.UserId!, operation.CommandId)
            : scope.IsGlobal
                ? KeyCodec.Encode(ScopedOutcomeSpace, "global", operation.UserId!, operation.CommandId)
                : KeyCodec.Encode(ScopedOutcomeSpace, "unknown", operation.UserId!, operation.CommandId);
    }

    internal static byte[] PartitionKey(PartitionRef partition, string principalId, Guid commandId)
        => KeyCodec.Encode(ScopedOutcomeSpace, partition.TenantId, partition.DatabaseId,
            partition.TransactionDomainId, partition.PartitionKey, principalId, commandId);

    internal static byte[] GlobalKey(string principalId, Guid commandId)
        => KeyCodec.Encode(ScopedOutcomeSpace, GlobalOutcomeScope, principalId, commandId);

    internal static byte[] UnknownKey(string principalId, Guid commandId)
        => KeyCodec.Encode(ScopedOutcomeSpace, UnknownOutcomeScope, principalId, commandId);

    internal static Result<object>? Read(IAtomicStore store, Command<DatabaseCommand> operation)
        => ReadStored(store, operation)?.Result;

    internal static StoredOutcome? ReadStored(IAtomicStore store, Command<DatabaseCommand> operation)
    {
        var key = Key(operation);
        return store.Read(view => view.GetRecord<StoredOutcome>(key));
    }

    internal static Result<object>? ReadPartition(IAtomicStore store, PartitionRef partition, string principalId, Guid commandId)
        => store.Read(view => view.GetRecord<StoredOutcome>(PartitionKey(partition, principalId, commandId))?.Result);

    internal static StoredOutcome ReadStored(DatabaseEngine database, Guid commandId, CancellationToken token)
    {
        var work = new ReadExecutionBudget(database.OperationLimitsOptions, database.EvaluationClock, token);
        return database.Store.Read(raw =>
        {
            var page = work.CreateView(raw).Scan(KeyCodec.Encode(ScopedOutcomeSpace), database.Limits.MaxScanRecords);
            if (page.HasMore)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, "The outcome oracle scan exceeded its record bound.");
            }
            var matches = page.Records.Select(row => NativeSerialization.Deserialize<StoredOutcome>(row.Value.Span))
                .Where(outcome => outcome.PreparedCommand.CommandId == commandId).ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidOperationException("The exact stored test outcome is missing or ambiguous.");
            }
            return matches.Single();
        });
    }

    private static (PartitionRef? Partition, bool IsGlobal) ReadExpectedScope(Command<DatabaseCommand> operation)
    {
        try
        {
            var partition = operation.Value!.Kind switch
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
            {
                return (partition, false);
            }
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

    private static bool IsGlobalOperation(Command<DatabaseCommand> operation)
        => operation.Value!.Kind switch
        {
            OperationKind.ConfigureResource => IsNonNull<ConfigureResourceRequest>(operation),
            OperationKind.ConfigurePrincipal => IsNonNull<ConfigurePrincipalRequest>(operation),
            OperationKind.ConfigureApiKey => IsNonNull<ConfigureApiKeyRequest>(operation),
            OperationKind.SetDispatch => IsBoolean(operation.Value!.Input),
            _ => false
        };

    private static bool IsBoolean(ReadOnlyMemory<byte> input)
    {
        _ = NativeSerialization.Deserialize<bool>(input.Span);
        return true;
    }

    private static PartitionRef? Partition<T>(Command<DatabaseCommand> operation, Func<T, PartitionRef?> select)
        where T : class
    {
        var request = NativeSerialization.Deserialize<T>(operation.Value!.Input.Span, NativeValidationProfile.PublicInputElements);
        return request is null ? null : select(request);
    }

    private static bool IsNonNull<T>(Command<DatabaseCommand> operation) where T : class
        => NativeSerialization.Deserialize<T>(operation.Value!.Input.Span, NativeValidationProfile.PublicInputElements) is not null;

    private static bool Valid(PartitionRef? partition)
        => partition is not null && !string.IsNullOrWhiteSpace(partition.TenantId)
            && !string.IsNullOrWhiteSpace(partition.DatabaseId)
            && !string.IsNullOrWhiteSpace(partition.TransactionDomainId)
            && !string.IsNullOrWhiteSpace(partition.PartitionKey);
}
