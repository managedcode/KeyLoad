namespace KeyLoad.Core;

/// <summary>Where a command is ordered: one atomic partition, the catalog, the authorization records or the runtime journal.</summary>
internal enum CommandRoute
{
    Partition,
    Catalog,
    Authorization,
    RuntimeJournal
}

/// <summary>A command's route, the identity its input must carry and its atomic partition.</summary>
internal readonly record struct CommandPartition(CommandRoute Route, Guid? CommandId, PartitionRef? Partition);

/// <summary>The one table from a typed command input to its stable identity and atomic partition.</summary>
internal static class CommandPartitions
{
    /// <summary>Reads the route of an input already admitted as the exact contract of its kind.</summary>
    internal static CommandPartition Of(OperationKind kind, object input) => kind switch
    {
        OperationKind.Bm25CorpusPublicationPhase => Partition<global::KeyLoad.Core.Search.Bm25CorpusPublicationPhaseCommand>(input, static value => (null, value.Current?.Partition)),
        OperationKind.OnlineTextPublicationPhase => Partition<global::KeyLoad.Core.Search.OnlineTextPublicationPhaseCommand>(input, static value => (null, value.Request?.Consumer?.Partition)),
        OperationKind.FileMetadata => Partition<global::KeyLoad.Core.BlobStorage.FileMetadataMutation>(input, static value => (value.CommandId, value.Address?.Partition)),
        OperationKind.Batch => Partition<CommandRequest>(input, static value => (value.CommandId, value.Partition)),
        OperationKind.Receive => Partition<ReceiveRequest>(input, static value => (value.RequestId, value.Lane?.Partition)),
        OperationKind.Delivery => Partition<DeliveryCommand>(input, static value => (value.CommandId, value.Lane?.Partition)),
        OperationKind.Processing => Partition<ProcessingRequest>(input, static value => (value.CommandId, value.Lane?.Partition)),
        OperationKind.CommitInbox => Partition<CommitInboxRequest>(input, static value => (value.CommandId, value.Target?.Partition)),
        OperationKind.PurgeOutbox => Partition<PurgeOutboxRequest>(input, static value => (value.CommandId, value.Partition)),
        OperationKind.ConfigureSubscription or OperationKind.SeekSubscription or OperationKind.ReceiveSubscription
            or OperationKind.SubscriptionDelivery or OperationKind.SubscriptionProcessing
            or OperationKind.SetSubscriptionPaused => Subscription(kind, input),
        OperationKind.ConfigureProjectionConsumer => Partition<ConfigureProjectionConsumerRequest>(input, static value => (value.CommandId, value.Consumer?.Partition)),
        OperationKind.CommitProjectionBatch => Partition<CommitProjectionBatchRequest>(input, static value => (value.CommandId, value.Consumer?.Partition)),
        OperationKind.ReleaseProjectionConsumer => Partition<ReleaseProjectionConsumerRequest>(input, static value => (value.CommandId, value.Consumer?.Partition)),
        OperationKind.ConfigureResource or OperationKind.CreateTable or OperationKind.DropTable
            or OperationKind.SetDispatch or OperationKind.SetQueueDispatch => new(CommandRoute.Catalog, null, null),
        OperationKind.ConfigurePrincipal or OperationKind.ConfigureApiKey => new(CommandRoute.Authorization, null, null),
        OperationKind.RuntimeJournal => new(CommandRoute.RuntimeJournal, null, null),
        _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, CommandExecutionValidation.UnsupportedDatabaseCommand)
    };

    private static CommandPartition Subscription(OperationKind kind, object input) => kind switch
    {
        OperationKind.ConfigureSubscription => Partition<ConfigureSubscriptionRequest>(input, static value => (value.CommandId, value.Subscription?.Source?.Partition)),
        OperationKind.SeekSubscription => Partition<SeekSubscriptionRequest>(input, static value => (value.CommandId, value.Subscription?.Source?.Partition)),
        OperationKind.ReceiveSubscription => Partition<ReceiveSubscriptionRequest>(input, static value => (value.RequestId, value.Subscription?.Source?.Partition)),
        OperationKind.SubscriptionDelivery => Partition<SubscriptionDeliveryCommand>(input, static value => (value.CommandId, value.Subscription?.Source?.Partition)),
        OperationKind.SubscriptionProcessing => Partition<SubscriptionProcessingRequest>(input, static value => (value.CommandId, value.Subscription?.Source?.Partition)),
        _ => Partition<SetSubscriptionPausedRequest>(input, static value => (value.CommandId, value.Subscription?.Source?.Partition))
    };

    private static CommandPartition Partition<T>(object input, Func<T, (Guid? CommandId, PartitionRef? Partition)> select)
    {
        var (commandId, partition) = select(input is T value ? value
            : throw Errors.Fail(ErrorCode.Corruption, KeyLoad.InternalSerialization.PayloadVersion.InvalidPayload));
        return new(CommandRoute.Partition, commandId, partition);
    }
}
