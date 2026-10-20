namespace KeyLoad.Core;
/// <summary>Executes CommandExecutionExecutionDatabaseCommandInputTypes operations against the caller's database context.</summary>
public static class CommandExecutionExecutionDatabaseCommandInputTypes
{
    internal static Type? OperationPayloadType(OperationKind kind) => kind switch
    {
        OperationKind.Bm25CorpusPublicationPhase => typeof(global::KeyLoad.Core.Features.Search.Bm25CorpusPublicationPhaseCommand),
        OperationKind.OnlineTextPublicationPhase => typeof(global::KeyLoad.Core.Features.Search.OnlineTextPublicationPhaseCommand),
        OperationKind.FileMetadata => typeof(global::KeyLoad.Core.Features.BlobStorage.FileMetadataMutation),
        OperationKind.Batch => typeof(CommandRequest),
        OperationKind.Receive => typeof(ReceiveRequest),
        OperationKind.Delivery => typeof(DeliveryCommand),
        OperationKind.Processing => typeof(ProcessingRequest),
        OperationKind.CommitInbox => typeof(CommitInboxRequest),
        OperationKind.ConfigureResource => typeof(ConfigureResourceRequest),
        OperationKind.CreateTable => typeof(CreateTableRequest),
        OperationKind.DropTable => typeof(DropTableRequest),
        OperationKind.SetQueueDispatch => typeof(QueueDispatchControlRequest),
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
        _ => null
    };
}
