using KeyLoad.Core.Features.CommandOutcomes.Contracts;
using KeyLoad.Core.Features.RuntimeJournaling.Contracts;
using KeyLoad.Core.Features.StoragePartitions.Contracts;
using KeyLoad.Features.InternalSerialization;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core.Features.CommandOutcomes.Identity;

internal static class CommandOutcomePartitionIdentity
{
    internal static CommandOutcomePartitionScope Resolve(Command<DatabaseCommand> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.Value!.Input.IsEmpty)
        {
            return CommandOutcomePartitionScope.Unknown;
        }

        return ResolveKind(operation.Value!.Kind, operation.Value.Input);
    }

    private static CommandOutcomePartitionScope ResolveKind(OperationKind kind, ReadOnlyMemory<byte> payload)
        => kind switch
        {
            OperationKind.Bm25CorpusPublicationPhase => ForPayload<global::KeyLoad.Core.Features.Search.Bm25CorpusPublicationPhaseCommand>(payload, static phase => phase.Current?.Partition),
            OperationKind.OnlineTextPublicationPhase => ForPayload<global::KeyLoad.Core.Features.Search.OnlineTextPublicationPhaseCommand>(payload, static request => request.Request.Consumer.Partition),
            OperationKind.Batch => ForPayload<CommandRequest>(payload, static request => request.Partition),
            OperationKind.Receive => ForPayload<ReceiveRequest>(payload, static request => request.Lane?.Partition),
            OperationKind.Delivery => ForPayload<DeliveryCommand>(payload, static request => request.Lane?.Partition),
            OperationKind.Processing => ForPayload<ProcessingRequest>(payload, static request => request.Lane?.Partition),
            OperationKind.CommitInbox => ForPayload<CommitInboxRequest>(payload, static request => request.Target?.Partition),
            OperationKind.PurgeOutbox => ForPayload<PurgeOutboxRequest>(payload, static request => request.Partition),
            OperationKind.ConfigureSubscription or OperationKind.SeekSubscription or OperationKind.ReceiveSubscription
                or OperationKind.SubscriptionDelivery or OperationKind.SubscriptionProcessing or OperationKind.SetSubscriptionPaused
                => ResolveSubscription(kind, payload),
            OperationKind.ConfigureProjectionConsumer or OperationKind.CommitProjectionBatch
                or OperationKind.ReleaseProjectionConsumer => ResolveProjection(kind, payload),
            OperationKind.DropTable or OperationKind.CreateTable or OperationKind.ConfigureResource or OperationKind.ConfigurePrincipal or OperationKind.ConfigureApiKey
                or OperationKind.SetQueueDispatch or OperationKind.SetDispatch => CommandOutcomePartitionScope.Global,
            _ => CommandOutcomePartitionScope.Unknown
        };

    private static CommandOutcomePartitionScope ResolveSubscription(OperationKind kind, ReadOnlyMemory<byte> payload)
        => kind switch
        {
            OperationKind.ConfigureSubscription => ForPayload<ConfigureSubscriptionRequest>(payload, static request => request.Subscription?.Source?.Partition),
            OperationKind.SeekSubscription => ForPayload<SeekSubscriptionRequest>(payload, static request => request.Subscription?.Source?.Partition),
            OperationKind.ReceiveSubscription => ForPayload<ReceiveSubscriptionRequest>(payload, static request => request.Subscription?.Source?.Partition),
            OperationKind.SubscriptionDelivery => ForPayload<SubscriptionDeliveryCommand>(payload, static request => request.Subscription?.Source?.Partition),
            OperationKind.SubscriptionProcessing => ForPayload<SubscriptionProcessingRequest>(payload, static request => request.Subscription?.Source?.Partition),
            OperationKind.SetSubscriptionPaused => ForPayload<SetSubscriptionPausedRequest>(payload, static request => request.Subscription?.Source?.Partition),
            _ => CommandOutcomePartitionScope.Unknown
        };

    private static CommandOutcomePartitionScope ResolveProjection(OperationKind kind, ReadOnlyMemory<byte> payload)
        => kind switch
        {
            OperationKind.ConfigureProjectionConsumer => ForPayload<ConfigureProjectionConsumerRequest>(payload, static request => request.Consumer?.Partition),
            OperationKind.CommitProjectionBatch => ForPayload<CommitProjectionBatchRequest>(payload, static request => request.Consumer?.Partition),
            OperationKind.ReleaseProjectionConsumer => ForPayload<ReleaseProjectionConsumerRequest>(payload, static request => request.Consumer?.Partition),
            _ => CommandOutcomePartitionScope.Unknown
        };

    private static CommandOutcomePartitionScope ForPayload<T>(ReadOnlyMemory<byte> payload,
        Func<T, PartitionRef?> partition) where T : class
    {
        var request = OrleansSerialization.Deserialize<T>(payload.Span, ValidationProfile.PublicInputElements);
        if (request is null)
        {
            return CommandOutcomePartitionScope.Unknown;
        }

        var value = partition(request);
        if (value is null)
        {
            return CommandOutcomePartitionScope.Unknown;
        }

        try
        {
            DatabaseEngine.ValidatePartition(value);
            return new(CommandOutcomeScopeKind.Partition, value);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Validation)
        {
            return CommandOutcomePartitionScope.Unknown;
        }
    }
}
