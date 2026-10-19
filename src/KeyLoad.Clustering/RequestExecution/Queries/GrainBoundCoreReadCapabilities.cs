using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Clustering;

/// <summary>Uses the same explicit original execution binding for ordinary model reads.</summary>
internal static class GrainBoundCoreReadCapabilities
{
    internal static object? Execute(DatabaseEngine database, GrainReadKind kind, string principal,
        ReadOnlyMemory<byte> payload, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding,
        PhysicalShardRecord? streamOwner)
    {
        budget.Check();
        return kind switch
        {
            GrainReadKind.Document => database.GetDocumentBound(principal, GrainPayload.Read<GetDocumentRequest>(payload), budget, binding),
            GrainReadKind.Stream => database.ReadStreamBound(principal, GrainPayload.Read<ReadStreamRequest>(payload),
                streamOwner ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest), budget, binding),
            GrainReadKind.EventSource => database.ReadEventSourceBound(principal, GrainPayload.Read<ReadEventSourceRequest>(payload), budget, binding),
            GrainReadKind.AggregateReplay => database.ReadAggregateReplayBound(principal, GrainPayload.Read<ReadAggregateReplayRequest>(payload), budget, binding),
            GrainReadKind.Traverse => database.TraverseBound(principal, GrainPayload.ReadPublicInput<TraverseRequest>(payload), budget, binding),
            GrainReadKind.GraphShortestPath => database.ShortestPathBound(principal, GrainPayload.ReadPublicInput<GraphShortestPathRequest>(payload), budget, binding),
            GrainReadKind.GraphIncomingEdges => database.ReadIncomingGraphEdgesBound(principal, GrainPayload.ReadPublicInput<ReadIncomingGraphEdgesRequest>(payload), budget, binding),
            GrainReadKind.ChangeFeed => database.ReadChangeFeedBound(principal, GrainPayload.Read<ReadChangeFeedRequest>(payload), budget, binding),
            GrainReadKind.ProjectionBatch => database.ReadProjectionBatchBound(principal, GrainPayload.Read<ReadProjectionBatchRequest>(payload), budget, binding),
            GrainReadKind.OutboxStatus => database.GetOutboxStatusBound(principal, GrainPayload.Read<GetOutboxStatusRequest>(payload).Partition, budget, binding),
            GrainReadKind.Samples or GrainReadKind.LatestSample or GrainReadKind.AggregateSamples
                or GrainReadKind.AggregateSampleWindows or GrainReadKind.SampleRetention or GrainReadKind.SampleRollup
                or GrainReadKind.SampleChunkWindow => Samples(database, kind, principal, payload, budget, binding),
            _ => Messaging(database, kind, principal, payload, budget, binding)
        };
    }

    private static object Samples(DatabaseEngine database, GrainReadKind kind, string principal,
        ReadOnlyMemory<byte> payload, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
        => kind switch
        {
            GrainReadKind.Samples => database.ReadSamplesBound(principal, GrainPayload.Read<ReadSamplesRequest>(payload), budget, binding),
            GrainReadKind.LatestSample => database.ReadLatestSampleBound(principal, GrainPayload.Read<ReadLatestSampleRequest>(payload), budget, binding),
            GrainReadKind.AggregateSamples => database.AggregateSamplesBound(principal, GrainPayload.Read<AggregateSamplesRequest>(payload), budget, binding),
            GrainReadKind.AggregateSampleWindows => database.AggregateSampleWindowsBound(principal, GrainPayload.Read<AggregateSampleWindowsRequest>(payload), budget, binding),
            GrainReadKind.SampleRetention => database.ReadSampleRetentionBound(principal, GrainPayload.Read<ReadSampleRetentionRequest>(payload), budget, binding),
            GrainReadKind.SampleRollup => database.ReadSampleRollupBound(principal, GrainPayload.Read<ReadSampleRollupRequest>(payload), budget, binding),
            GrainReadKind.SampleChunkWindow => database.ReadSampleChunkWindowBound(principal, GrainPayload.Read<ReadSampleChunkWindowRequest>(payload), budget, binding),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };

    private static object? Messaging(DatabaseEngine database, GrainReadKind kind, string principal,
        ReadOnlyMemory<byte> payload, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
        => kind switch
        {
            GrainReadKind.Subscription => database.GetSubscriptionBound(principal, GrainPayload.Read<GetSubscriptionRequest>(payload), budget, binding),
            GrainReadKind.Message => database.InspectMessageBound(principal, GrainPayload.Read<InspectMessageRequest>(payload), budget, binding),
            GrainReadKind.RecurringSchedule => database.InspectRecurringScheduleBound(principal, GrainPayload.Read<InspectRecurringScheduleRequest>(payload), budget, binding),
            GrainReadKind.Saga => database.InspectSagaBound(principal, GrainPayload.Read<InspectSagaRequest>(payload), budget, binding),
            GrainReadKind.QueueTransfer => database.InspectQueueTransferBound(principal, GrainPayload.Read<InspectQueueTransferRequest>(payload), budget, binding),
            GrainReadKind.QueueTransferReceipt => database.InspectQueueTransferReceiptBound(principal, GrainPayload.Read<InspectQueueTransferReceiptRequest>(payload), budget, binding),
            GrainReadKind.QueueTransferCoordination => database.ReadRemoteTransferCoordinationBound(principal, GrainPayload.Read<RemoteTransferCoordinationReadRequest>(payload), budget, binding),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
}
