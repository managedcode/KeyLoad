using KeyLoad.Core;

namespace KeyLoad.Orleans;

internal sealed class GrainCoreReadCapabilities(DatabaseEngine database)
{
    internal object? Execute(GrainReadKind kind, string principal, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return kind switch
        {
            GrainReadKind.Document => Document(principal, GrainNativePayload.Read<GetDocumentRequest>(payload), cancellationToken),
            GrainReadKind.Stream => Stream(principal, GrainNativePayload.Read<ReadStreamRequest>(payload), cancellationToken),
            GrainReadKind.EventSource => database.ReadEventSource(principal, GrainNativePayload.Read<ReadEventSourceRequest>(payload), cancellationToken),
            GrainReadKind.Subscription => database.GetSubscription(principal, GrainNativePayload.Read<GetSubscriptionRequest>(payload).Subscription),
            GrainReadKind.Message => Message(principal, GrainNativePayload.Read<InspectMessageRequest>(payload)),
            GrainReadKind.QueueTransfer => Transfer(principal, GrainNativePayload.Read<InspectQueueTransferRequest>(payload), cancellationToken),
            GrainReadKind.QueueTransferReceipt => TransferReceipt(principal,
                GrainNativePayload.Read<InspectQueueTransferReceiptRequest>(payload), cancellationToken),
            GrainReadKind.RecurringSchedule => Schedule(principal,
                GrainNativePayload.Read<InspectRecurringScheduleRequest>(payload), cancellationToken),
            GrainReadKind.Saga => Saga(principal, GrainNativePayload.Read<InspectSagaRequest>(payload), cancellationToken),
            GrainReadKind.Traverse => Traverse(principal, GrainNativePayload.ReadPublicInput<TraverseRequest>(payload), cancellationToken),
            GrainReadKind.GraphShortestPath => database.ShortestPath(principal,
                GrainNativePayload.ReadPublicInput<GraphShortestPathRequest>(payload), cancellationToken: cancellationToken),
            GrainReadKind.GraphIncomingEdges => database.ReadIncomingGraphEdges(principal,
                GrainNativePayload.ReadPublicInput<ReadIncomingGraphEdgesRequestV1>(payload), cancellationToken: cancellationToken),
            GrainReadKind.Samples => Samples(principal, GrainNativePayload.Read<ReadSamplesRequest>(payload), cancellationToken),
            GrainReadKind.LatestSample => database.ReadLatestSample(principal, GrainNativePayload.Read<ReadLatestSampleRequest>(payload), cancellationToken),
            GrainReadKind.AggregateSamples => database.AggregateSamples(principal, GrainNativePayload.Read<AggregateSamplesRequest>(payload), cancellationToken),
            GrainReadKind.AggregateSampleWindows => database.AggregateSampleWindows(principal,
                GrainNativePayload.Read<AggregateSampleWindowsRequest>(payload), cancellationToken),
            GrainReadKind.SampleRollup => database.ReadSampleRollup(principal,
                GrainNativePayload.Read<ReadSampleRollupRequest>(payload), cancellationToken),
            GrainReadKind.SampleRetention => database.ReadSampleRetention(principal,
                GrainNativePayload.Read<ReadSampleRetentionRequest>(payload), cancellationToken),
            GrainReadKind.AggregateReplay => database.ReadAggregateReplay(principal,
                GrainNativePayload.Read<ReadAggregateReplayRequest>(payload), cancellationToken),
            GrainReadKind.ChangeFeed => database.ReadChangeFeed(principal, GrainNativePayload.Read<ReadChangeFeedRequest>(payload), cancellationToken),
            GrainReadKind.OutboxStatus => database.GetOutboxStatus(principal, GrainNativePayload.Read<GetOutboxStatusRequest>(payload).Partition),
            GrainReadKind.ProjectionBatch => database.ReadProjectionBatch(principal, GrainNativePayload.Read<ReadProjectionBatchRequest>(payload)),
            GrainReadKind.AtomicPartitionPlacement => database.ReadAtomicPartitionPlacement(principal,
                GrainNativePayload.ReadPublicInput<AtomicPartitionPlacementReadRequest>(payload)),
            GrainReadKind.OwnedDocument => OwnedDocument(principal,
                GrainNativePayload.Read<OwnedDocumentReadRequestV1>(payload), cancellationToken),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
    }

    internal static bool Handles(GrainReadKind kind) => kind is >= GrainReadKind.Document and <= GrainReadKind.Samples
        or GrainReadKind.ChangeFeed or GrainReadKind.OutboxStatus or GrainReadKind.ProjectionBatch
        or GrainReadKind.LatestSample or GrainReadKind.AggregateSamples or GrainReadKind.AggregateSampleWindows
        or GrainReadKind.SampleRetention or GrainReadKind.SampleRollup or GrainReadKind.AggregateReplay
        or GrainReadKind.QueueTransfer or GrainReadKind.QueueTransferReceipt
        or GrainReadKind.RecurringSchedule or GrainReadKind.Saga or GrainReadKind.GraphShortestPath
        or GrainReadKind.AtomicPartitionPlacement or GrainReadKind.GraphIncomingEdges or GrainReadKind.OwnedDocument;

    private OwnedDocumentReadResultV1 OwnedDocument(string principal,
        OwnedDocumentReadRequestV1 request, CancellationToken cancellationToken)
        => database.ReadOwnedDocument(principal, request.Tenant, request.Request, request.Owner, cancellationToken);

    private DocumentResult? Document(string principal, GetDocumentRequest request, CancellationToken cancellationToken)
        => database.GetDocument(principal, request.Reference, request.MinimumToken, cancellationToken);

    private StreamPage Stream(string principal, ReadStreamRequest request, CancellationToken cancellationToken)
        => database.ReadStream(principal, request.Stream, request.AfterRevision, request.Limit, cancellationToken);

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
