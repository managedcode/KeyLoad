using KeyLoad.Core;
using KeyLoad.Core.Messaging;
using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.Clustering;

internal sealed class GrainCoreReadCapabilities(DatabaseEngine database)
{
    internal object? Execute(GrainReadKind kind, string principal, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken, PhysicalShardRecord? streamOwner = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return kind switch
        {
            GrainReadKind.Document => Document(principal, GrainPayload.Read<GetDocumentRequest>(payload), cancellationToken),
            GrainReadKind.Stream => Stream(principal, GrainPayload.Read<ReadStreamRequest>(payload), streamOwner, cancellationToken),
            GrainReadKind.EventSource => database.ReadEventSource(principal, GrainPayload.Read<ReadEventSourceRequest>(payload), cancellationToken),
            GrainReadKind.Subscription => database.GetSubscription(principal, GrainPayload.Read<GetSubscriptionRequest>(payload).Subscription),
            GrainReadKind.Message => Message(principal, GrainPayload.Read<InspectMessageRequest>(payload)),
            GrainReadKind.QueueTransferCoordination => database.ReadRemoteTransferCoordination(principal,
                GrainPayload.Read<RemoteTransferCoordinationReadRequest>(payload), cancellationToken),
            GrainReadKind.QueueTransfer => Transfer(principal, GrainPayload.Read<InspectQueueTransferRequest>(payload), cancellationToken),
            GrainReadKind.QueueTransferReceipt => TransferReceipt(principal,
                GrainPayload.Read<InspectQueueTransferReceiptRequest>(payload), cancellationToken),
            GrainReadKind.RecurringSchedule => Schedule(principal,
                GrainPayload.Read<InspectRecurringScheduleRequest>(payload), cancellationToken),
            GrainReadKind.Saga => Saga(principal, GrainPayload.Read<InspectSagaRequest>(payload), cancellationToken),
            GrainReadKind.Traverse => Traverse(principal, GrainPayload.ReadPublicInput<TraverseRequest>(payload), cancellationToken),
            GrainReadKind.GraphShortestPath => database.ShortestPath(principal,
                GrainPayload.ReadPublicInput<GraphShortestPathRequest>(payload), cancellationToken: cancellationToken),
            GrainReadKind.GraphIncomingEdges => database.ReadIncomingGraphEdges(principal,
                GrainPayload.ReadPublicInput<ReadIncomingGraphEdgesRequest>(payload), cancellationToken: cancellationToken),
            GrainReadKind.Samples => Samples(principal, GrainPayload.Read<ReadSamplesRequest>(payload), cancellationToken),
            GrainReadKind.LatestSample => database.ReadLatestSample(principal, GrainPayload.Read<ReadLatestSampleRequest>(payload), cancellationToken),
            GrainReadKind.AggregateSamples => database.AggregateSamples(principal, GrainPayload.Read<AggregateSamplesRequest>(payload), cancellationToken),
            GrainReadKind.AggregateSampleWindows => database.AggregateSampleWindows(principal,
                GrainPayload.Read<AggregateSampleWindowsRequest>(payload), cancellationToken),
            GrainReadKind.SampleRollup => database.ReadSampleRollup(principal,
                GrainPayload.Read<ReadSampleRollupRequest>(payload), cancellationToken),
            GrainReadKind.SampleChunkWindow => database.ReadSampleChunkWindow(principal,
                GrainPayload.Read<ReadSampleChunkWindowRequest>(payload), cancellationToken),
            GrainReadKind.SampleRetention => database.ReadSampleRetention(principal,
                GrainPayload.Read<ReadSampleRetentionRequest>(payload), cancellationToken),
            GrainReadKind.AggregateReplay => database.ReadAggregateReplay(principal,
                GrainPayload.Read<ReadAggregateReplayRequest>(payload), cancellationToken),
            GrainReadKind.ChangeFeed => database.ReadChangeFeed(principal, GrainPayload.Read<ReadChangeFeedRequest>(payload), cancellationToken),
            GrainReadKind.OutboxStatus => database.GetOutboxStatus(principal, GrainPayload.Read<GetOutboxStatusRequest>(payload).Partition),
            GrainReadKind.ProjectionBatch => database.ReadProjectionBatch(principal, GrainPayload.Read<ReadProjectionBatchRequest>(payload)),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
    }

    internal object? ExecuteBound(GrainReadKind kind, string principal, ReadOnlyMemory<byte> payload,
        ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding, PhysicalShardRecord? streamOwner)
        => GrainBoundCoreReadCapabilities.Execute(database, kind, principal, payload, budget, binding, streamOwner);

    internal static bool Handles(GrainReadKind kind) => kind is >= GrainReadKind.Document and <= GrainReadKind.Samples
        or GrainReadKind.ChangeFeed or GrainReadKind.OutboxStatus or GrainReadKind.ProjectionBatch
        or GrainReadKind.LatestSample or GrainReadKind.AggregateSamples or GrainReadKind.AggregateSampleWindows
        or GrainReadKind.SampleRetention or GrainReadKind.SampleRollup or GrainReadKind.SampleChunkWindow or GrainReadKind.AggregateReplay
        or GrainReadKind.QueueTransfer or GrainReadKind.QueueTransferReceipt or GrainReadKind.QueueTransferCoordination
        or GrainReadKind.RecurringSchedule or GrainReadKind.Saga or GrainReadKind.GraphShortestPath
        or GrainReadKind.GraphIncomingEdges;

    private DocumentResult? Document(string principal, GetDocumentRequest request, CancellationToken cancellationToken)
        => database.GetDocument(principal, request.Reference, request.MinimumToken, cancellationToken);

    private StreamPage Stream(string principal, ReadStreamRequest request, PhysicalShardRecord? streamOwner,
        CancellationToken cancellationToken)
        => streamOwner is null ? database.ReadStream(principal, request, cancellationToken)
            : database.ReadStream(principal, request, streamOwner, cancellationToken);

    private MessageInspection? Message(string principal, InspectMessageRequest request)
        => database.InspectMessage(principal, request.Lane, request.Id);

    private QueueTransferInspection? Transfer(string principal, InspectQueueTransferRequest request, CancellationToken cancellationToken)
        => database.InspectQueueTransfer(principal, request.SourceQueue, request.TransferId, cancellationToken);

    private QueueTransferReceiptInspection? TransferReceipt(string principal, InspectQueueTransferReceiptRequest request,
        CancellationToken cancellationToken)
        => database.InspectQueueTransferReceipt(principal, request.DestinationQueue, request.SourceQueue, request.TransferId, cancellationToken);

    private GraphTraversal Traverse(string principal, TraverseRequest request, CancellationToken cancellationToken)
        => database.Traverse(principal, request.Partition, request.Graph, request.Start, request.MaxDepth,
            request.MaxVertices, request.MaxEdges, request.Labels?.ToArray(), cancellationToken);

    private RecurringScheduleInspection? Schedule(string principal, InspectRecurringScheduleRequest request,
        CancellationToken cancellationToken)
        => database.InspectRecurringSchedule(principal, request.Lane, request.ScheduleId, cancellationToken);

    private SagaInspection? Saga(string principal, InspectSagaRequest request, CancellationToken cancellationToken)
        => database.InspectSaga(principal, request.Lane, request.SagaId, cancellationToken);

    private SampleRecord[] Samples(string principal, ReadSamplesRequest request, CancellationToken cancellationToken)
        => database.ReadSamples(principal, request.Partition, request.Set, request.SeriesId,
            request.From, request.Until, request.Limit, cancellationToken);
}
