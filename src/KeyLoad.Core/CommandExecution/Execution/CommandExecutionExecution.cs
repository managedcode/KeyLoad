using System.Text.Json;
using KeyLoad.Core.InternalSerialization;
using KeyLoad.InternalSerialization;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes CommandExecutionExecution operations against the caller's database context.</summary>
public static class CommandExecutionExecution
{
    /// <summary>Creates the execution communication command from typed, generated database input.</summary>
    /// <param name = "kind">The exact command capability.</param>
    /// <param name = "id">Stable command identity.</param>
    /// <param name = "principalId">Persisted principal selected by the caller's trusted boundary.</param>
    /// <param name = "evaluatedAt">Original operation evaluation time.</param>
    /// <param name = "payload">Execution attributed payload for this capability.</param>
    /// <returns>An owned execution command retaining the original input fingerprint.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static Command<DatabaseCommand> CreateCommand(this DatabaseEngine engine, OperationKind kind, Guid id, string principalId, DateTimeOffset evaluatedAt, ReadOnlyMemory<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.CreateCommandCore(kind, id, principalId, evaluatedAt, payload, null);
    }

    internal static Command<DatabaseCommand> CreateCommand(this DatabaseEngine engine, OperationKind kind, Guid id, string principalId, DateTimeOffset evaluatedAt, ReadOnlyMemory<byte> payload, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        return engine.CreateCommandCore(kind, id, principalId, evaluatedAt, payload, work);
    }

    internal static Command<DatabaseCommand> CreateCommand(this DatabaseEngine engine, IKeyValueView admissionView, OperationKind kind, Guid id, string principalId, DateTimeOffset evaluatedAt, ReadOnlyMemory<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(admissionView);
        return engine.CreateCommandCore(kind, id, principalId, evaluatedAt, payload, null, admissionView);
    }

    internal static Command<DatabaseCommand> CreateCommandCore(this DatabaseEngine engine, OperationKind kind, Guid id, string principalId, DateTimeOffset evaluatedAt, ReadOnlyMemory<byte> payload, ReadExecutionBudget? work, IKeyValueView? admissionView = null)
    {
        payload = payload.ToArray();
        var identity = kind switch
        {
            OperationKind.FileMetadata => Identity<global::KeyLoad.Core.BlobStorage.FileMetadataMutation>(payload),
            OperationKind.Batch => CommandExecutionExecution.BatchIdentity(payload, work),
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
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, CommandExecutionValidation.UnsupportedDatabaseCommand)
        };
        return engine.CreatePreparedCommand(kind, id, principalId, evaluatedAt, payload, DatabaseCommandFingerprint.Compute(id, kind, principalId, identity), work, admissionView);
    }

    internal static string BatchIdentity(ReadOnlyMemory<byte> payload, ReadExecutionBudget? work)
    {
        var request = OrleansSerialization.Deserialize<CommandRequest>(payload.Span, ValidationProfile.PublicInputElements);
        if (!AnalyticsIdentity.HasAnalyticsMutation(request))
        {
            return JsonSerializer.Serialize(request, JsonDefaults.Options);
        }

        if (work is null)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, AnalyticsWorkRequired);
        }

        return AnalyticsIdentity.Canonicalize(request, work);
    }

    internal const string AnalyticsWorkRequired = "Execution analytics issuance requires the original operation budget.";
    internal static string Identity<T>(ReadOnlyMemory<byte> payload) => JsonSerializer.Serialize(OrleansSerialization.Deserialize<T>(payload.Span, ValidationProfile.PublicInputElements), JsonDefaults.Options);
}
