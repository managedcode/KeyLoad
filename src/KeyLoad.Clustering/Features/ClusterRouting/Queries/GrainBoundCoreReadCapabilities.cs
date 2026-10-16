using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Clustering;

/// <summary>Uses the same explicit original native binding for ordinary model reads.</summary>
internal static class GrainBoundCoreReadCapabilities
{
    internal static object? Execute(DatabaseEngine database, GrainReadKind kind, string principal,
        ReadOnlyMemory<byte> payload, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding,
        PhysicalShardRecord? streamOwner)
    {
        budget.Check();
        return kind switch
        {
            GrainReadKind.AtomicPartitionPlacement => database.ReadAtomicPartitionPlacement(principal,
                GrainNativePayload.ReadPublicInput<AtomicPartitionPlacementReadRequest>(payload)),
            GrainReadKind.Document => database.GetDocumentBound(principal, GrainNativePayload.Read<GetDocumentRequest>(payload), budget, binding),
            GrainReadKind.Stream => database.ReadStreamBound(principal, GrainNativePayload.Read<ReadStreamRequest>(payload),
                streamOwner ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest), budget, binding),
            GrainReadKind.EventSource => database.ReadEventSourceBound(principal, GrainNativePayload.Read<ReadEventSourceRequest>(payload), budget, binding),
            GrainReadKind.AggregateReplay => database.ReadAggregateReplayBound(principal, GrainNativePayload.Read<ReadAggregateReplayRequest>(payload), budget, binding),
            GrainReadKind.Traverse => database.TraverseBound(principal, GrainNativePayload.ReadPublicInput<TraverseRequest>(payload), budget, binding),
            GrainReadKind.GraphShortestPath => database.ShortestPathBound(principal, GrainNativePayload.ReadPublicInput<GraphShortestPathRequest>(payload), budget, binding),
            GrainReadKind.GraphIncomingEdges => database.ReadIncomingGraphEdgesBound(principal, GrainNativePayload.ReadPublicInput<ReadIncomingGraphEdgesRequestV1>(payload), budget, binding),
            GrainReadKind.ChangeFeed => database.ReadChangeFeedBound(principal, GrainNativePayload.Read<ReadChangeFeedRequest>(payload), budget, binding),
            GrainReadKind.ProjectionBatch => database.ReadProjectionBatchBound(principal, GrainNativePayload.Read<ReadProjectionBatchRequest>(payload), budget, binding),
            GrainReadKind.OutboxStatus => database.GetOutboxStatusBound(principal, GrainNativePayload.Read<GetOutboxStatusRequest>(payload).Partition, budget, binding),
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
            GrainReadKind.Samples => database.ReadSamplesBound(principal, GrainNativePayload.Read<ReadSamplesRequest>(payload), budget, binding),
            GrainReadKind.LatestSample => database.ReadLatestSampleBound(principal, GrainNativePayload.Read<ReadLatestSampleRequest>(payload), budget, binding),
            GrainReadKind.AggregateSamples => database.AggregateSamplesBound(principal, GrainNativePayload.Read<AggregateSamplesRequest>(payload), budget, binding),
            GrainReadKind.AggregateSampleWindows => database.AggregateSampleWindowsBound(principal, GrainNativePayload.Read<AggregateSampleWindowsRequest>(payload), budget, binding),
            GrainReadKind.SampleRetention => database.ReadSampleRetentionBound(principal, GrainNativePayload.Read<ReadSampleRetentionRequest>(payload), budget, binding),
            GrainReadKind.SampleRollup => database.ReadSampleRollupBound(principal, GrainNativePayload.Read<ReadSampleRollupRequest>(payload), budget, binding),
            GrainReadKind.SampleChunkWindow => database.ReadSampleChunkWindowBound(principal, GrainNativePayload.Read<ReadSampleChunkWindowRequest>(payload), budget, binding),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };

    private static object? Messaging(DatabaseEngine database, GrainReadKind kind, string principal,
        ReadOnlyMemory<byte> payload, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
        => kind switch
        {
            GrainReadKind.Subscription => database.GetSubscriptionBound(principal, GrainNativePayload.Read<GetSubscriptionRequest>(payload), budget, binding),
            GrainReadKind.Message => database.InspectMessageBound(principal, GrainNativePayload.Read<InspectMessageRequest>(payload), budget, binding),
            GrainReadKind.RecurringSchedule => database.InspectRecurringScheduleBound(principal, GrainNativePayload.Read<InspectRecurringScheduleRequest>(payload), budget, binding),
            GrainReadKind.Saga => database.InspectSagaBound(principal, GrainNativePayload.Read<InspectSagaRequest>(payload), budget, binding),
            GrainReadKind.QueueTransfer => database.InspectQueueTransferBound(principal, GrainNativePayload.Read<InspectQueueTransferRequest>(payload), budget, binding),
            GrainReadKind.QueueTransferReceipt => database.InspectQueueTransferReceiptBound(principal, GrainNativePayload.Read<InspectQueueTransferReceiptRequest>(payload), budget, binding),
            GrainReadKind.QueueTransferCoordination => database.ReadRemoteTransferCoordinationBound(principal, GrainNativePayload.Read<RemoteTransferCoordinationReadRequest>(payload), budget, binding),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
}
