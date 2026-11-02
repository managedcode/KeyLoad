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
    internal static CommandPartition Of(OperationKind kind, object /* KLD0041: This native command admission receives the exact Kind-selected business input before storing it in the existing persisted envelope. */ input) => kind switch
    {
        OperationKind.Bm25CorpusPublicationPhase => Partition<global::KeyLoad.Core.Search.Bm25CorpusPublicationPhaseCommand>(input, static value => (null, value.Current?.Partition)) /* KLD0041: The persisted command envelope carries the input selected by Kind; this routing step verifies and reads that native input. */,
        OperationKind.OnlineTextPublicationPhase => Partition<global::KeyLoad.Core.Search.OnlineTextPublicationPhaseCommand>(input, static value => (null, value.Request?.Consumer?.Partition)) /* KLD0041: The persisted command envelope carries the input selected by Kind; this routing step verifies and reads that native input. */,
        OperationKind.FileMetadata => Partition<global::KeyLoad.Core.BlobStorage.FileMetadataMutation>(input, static value => (value.CommandId, value.Address?.Partition)) /* KLD0041: The persisted command envelope carries the input selected by Kind; this routing step verifies and reads that native input. */,
        OperationKind.Batch => Partition<CommandRequest>(input, static value => (value.CommandId, value.Partition)) /* KLD0041: The persisted command envelope carries the input selected by Kind; this routing step verifies and reads that native input. */,
        OperationKind.Receive => Partition<ReceiveRequest>(input, static value => (value.RequestId, value.Lane?.Partition)) /* KLD0041: The persisted command envelope carries the input selected by Kind; this routing step verifies and reads that native input. */,
        OperationKind.Delivery => Partition<DeliveryCommand>(input, static value => (value.CommandId, value.Lane?.Partition)) /* KLD0041: The persisted command envelope carries the input selected by Kind; this routing step verifies and reads that native input. */,
        OperationKind.Processing => Partition<ProcessingRequest>(input, static value => (value.CommandId, value.Lane?.Partition)) /* KLD0041: The persisted command envelope carries the input selected by Kind; this routing step verifies and reads that native input. */,
        OperationKind.CommitInbox => Partition<CommitInboxRequest>(input, static value => (value.CommandId, value.Target?.Partition)) /* KLD0041: The persisted command envelope carries the input selected by Kind; this routing step verifies and reads that native input. */,
        OperationKind.PurgeOutbox => Partition<PurgeOutboxRequest>(input, static value => (value.CommandId, value.Partition)) /* KLD0041: The persisted command envelope carries the input selected by Kind; this routing step verifies and reads that native input. */,
        OperationKind.ConfigureSubscription or OperationKind.SeekSubscription or OperationKind.ReceiveSubscription
            or OperationKind.SubscriptionDelivery or OperationKind.SubscriptionProcessing
            or OperationKind.SetSubscriptionPaused => Subscription(kind, input) /* KLD0041: The persisted command envelope carries the input selected by Kind; this routing step verifies and reads that native input. */,
        OperationKind.ConfigureProjectionConsumer => Partition<ConfigureProjectionConsumerRequest>(input, static value => (value.CommandId, value.Consumer?.Partition)) /* KLD0041: The persisted command envelope carries the input selected by Kind; this routing step verifies and reads that native input. */,
        OperationKind.CommitProjectionBatch => Partition<CommitProjectionBatchRequest>(input, static value => (value.CommandId, value.Consumer?.Partition)) /* KLD0041: The persisted command envelope carries the input selected by Kind; this routing step verifies and reads that native input. */,
        OperationKind.ReleaseProjectionConsumer => Partition<ReleaseProjectionConsumerRequest>(input, static value => (value.CommandId, value.Consumer?.Partition)) /* KLD0041: The persisted command envelope carries the input selected by Kind; this routing step verifies and reads that native input. */,
        OperationKind.ConfigureResource or OperationKind.CreateTable or OperationKind.DropTable
            or OperationKind.SetDispatch or OperationKind.SetQueueDispatch => new(CommandRoute.Catalog, null, null),
        OperationKind.ConfigurePrincipal or OperationKind.ConfigureApiKey => new(CommandRoute.Authorization, null, null),
        OperationKind.RuntimeJournal => new(CommandRoute.RuntimeJournal, null, null),
        _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, CommandExecutionValidation.UnsupportedDatabaseCommand)
    };

    private static CommandPartition Subscription(OperationKind kind, object /* KLD0041: This native command admission receives the exact Kind-selected business input before storing it in the existing persisted envelope. */ input) => kind switch
    {
        OperationKind.ConfigureSubscription => Partition<ConfigureSubscriptionRequest>(input, static value => (value.CommandId, value.Subscription?.Source?.Partition)) /* KLD0041: The persisted command envelope carries the input selected by Kind; this routing step verifies and reads that native input. */,
        OperationKind.SeekSubscription => Partition<SeekSubscriptionRequest>(input, static value => (value.CommandId, value.Subscription?.Source?.Partition)) /* KLD0041: The persisted command envelope carries the input selected by Kind; this routing step verifies and reads that native input. */,
        OperationKind.ReceiveSubscription => Partition<ReceiveSubscriptionRequest>(input, static value => (value.RequestId, value.Subscription?.Source?.Partition)) /* KLD0041: The persisted command envelope carries the input selected by Kind; this routing step verifies and reads that native input. */,
        OperationKind.SubscriptionDelivery => Partition<SubscriptionDeliveryCommand>(input, static value => (value.CommandId, value.Subscription?.Source?.Partition)) /* KLD0041: The persisted command envelope carries the input selected by Kind; this routing step verifies and reads that native input. */,
        OperationKind.SubscriptionProcessing => Partition<SubscriptionProcessingRequest>(input, static value => (value.CommandId, value.Subscription?.Source?.Partition)) /* KLD0041: The persisted command envelope carries the input selected by Kind; this routing step verifies and reads that native input. */,
        _ => Partition<SetSubscriptionPausedRequest>(input, static value => (value.CommandId, value.Subscription?.Source?.Partition)) /* KLD0041: The persisted command envelope carries the input selected by Kind; this routing step verifies and reads that native input. */
    };

    private static CommandPartition Partition<T>(object /* KLD0041: This native command admission receives the exact Kind-selected business input before storing it in the existing persisted envelope. */ input, Func<T, (Guid? CommandId, PartitionRef? Partition)> select)
    {
        var (commandId, partition) = select(input is T value /* KLD0042: The admitted persisted input must match the Kind-selected business contract before its partition and command identity are read. */ ? value
            : throw Errors.Fail(ErrorCode.Corruption, KeyLoad.InternalSerialization.PayloadVersion.InvalidPayload));
        return new(CommandRoute.Partition, commandId, partition);
    }
}
