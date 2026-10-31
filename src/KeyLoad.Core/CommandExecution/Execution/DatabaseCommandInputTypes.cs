using System.Collections.Frozen;
using KeyLoad.InternalSerialization;

namespace KeyLoad.Core;
/// <summary>Maps every database command kind to its one typed input contract.</summary>
public static class DatabaseCommandInputTypes
{
    private static readonly FrozenDictionary<OperationKind, Type> Inputs = new Dictionary<OperationKind, Type>
    {
        [OperationKind.Bm25CorpusPublicationPhase] = typeof(global::KeyLoad.Core.Search.Bm25CorpusPublicationPhaseCommand),
        [OperationKind.OnlineTextPublicationPhase] = typeof(global::KeyLoad.Core.Search.OnlineTextPublicationPhaseCommand),
        [OperationKind.FileMetadata] = typeof(global::KeyLoad.Core.BlobStorage.FileMetadataMutation),
        [OperationKind.Batch] = typeof(CommandRequest),
        [OperationKind.Receive] = typeof(ReceiveRequest),
        [OperationKind.Delivery] = typeof(DeliveryCommand),
        [OperationKind.Processing] = typeof(ProcessingRequest),
        [OperationKind.CommitInbox] = typeof(CommitInboxRequest),
        [OperationKind.ConfigureResource] = typeof(ConfigureResourceRequest),
        [OperationKind.CreateTable] = typeof(CreateTableRequest),
        [OperationKind.DropTable] = typeof(DropTableRequest),
        [OperationKind.SetQueueDispatch] = typeof(QueueDispatchControlRequest),
        [OperationKind.ConfigurePrincipal] = typeof(ConfigurePrincipalRequest),
        [OperationKind.ConfigureApiKey] = typeof(ConfigureApiKeyRequest),
        [OperationKind.SetDispatch] = typeof(bool),
        [OperationKind.RuntimeJournal] = typeof(RuntimeJournalMutation),
        [OperationKind.ConfigureSubscription] = typeof(ConfigureSubscriptionRequest),
        [OperationKind.SeekSubscription] = typeof(SeekSubscriptionRequest),
        [OperationKind.ReceiveSubscription] = typeof(ReceiveSubscriptionRequest),
        [OperationKind.SubscriptionDelivery] = typeof(SubscriptionDeliveryCommand),
        [OperationKind.SubscriptionProcessing] = typeof(SubscriptionProcessingRequest),
        [OperationKind.SetSubscriptionPaused] = typeof(SetSubscriptionPausedRequest),
        [OperationKind.ConfigureProjectionConsumer] = typeof(ConfigureProjectionConsumerRequest),
        [OperationKind.CommitProjectionBatch] = typeof(CommitProjectionBatchRequest),
        [OperationKind.ReleaseProjectionConsumer] = typeof(ReleaseProjectionConsumerRequest),
        [OperationKind.PurgeOutbox] = typeof(PurgeOutboxRequest),
    }.ToFrozenDictionary();

    internal static Type For(OperationKind kind)
        => TryFind(kind, out var type) ? type
            : throw Errors.Fail(ErrorCode.UnsupportedCapability, CommandExecutionValidation.UnsupportedDatabaseCommand);

    internal static bool TryFind(OperationKind kind, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Type? type)
        => Inputs.TryGetValue(kind, out type);

    /// <summary>Admits exactly the declared input contract of one command kind.</summary>
    internal static object Require(OperationKind kind, object? input)
    {
        var type = For(kind);
        if (input is null || input.GetType() != type)
        {
            throw Errors.Fail(ErrorCode.Corruption, PayloadVersion.InvalidPayload);
        }
        ContractValidation.Validate(input, ValidationProfile.PublicInputElements);
        return input;
    }
}
