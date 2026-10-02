using KeyLoad.Core;

namespace KeyLoad.Orleans;

internal sealed class GrainCoreReadCapabilities(DatabaseEngine database)
{
    internal object? Execute(GrainReadKind kind, string principal, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return kind switch
        {
            GrainReadKind.Document => database.GetDocument(principal, GrainPayloadJson.Read<GetDocumentRequest>(payload).Reference),
            GrainReadKind.Stream => Stream(principal, GrainPayloadJson.Read<ReadStreamRequest>(payload), cancellationToken),
            GrainReadKind.EventSource => database.ReadEventSource(principal, GrainPayloadJson.Read<ReadEventSourceRequest>(payload), cancellationToken),
            GrainReadKind.Subscription => database.GetSubscription(principal, GrainPayloadJson.Read<GetSubscriptionRequest>(payload).Subscription),
            GrainReadKind.Message => Message(principal, GrainPayloadJson.Read<InspectMessageRequest>(payload)),
            GrainReadKind.Traverse => Traverse(principal, GrainPayloadJson.Read<TraverseRequest>(payload), cancellationToken),
            GrainReadKind.Samples => Samples(principal, GrainPayloadJson.Read<ReadSamplesRequest>(payload), cancellationToken),
            GrainReadKind.LatestSample => database.ReadLatestSample(principal, GrainPayloadJson.Read<ReadLatestSampleRequest>(payload), cancellationToken),
            GrainReadKind.AggregateSamples => database.AggregateSamples(principal, GrainPayloadJson.Read<AggregateSamplesRequest>(payload), cancellationToken),
            GrainReadKind.AggregateSampleWindows => database.AggregateSampleWindows(principal,
                GrainPayloadJson.Read<AggregateSampleWindowsRequest>(payload), cancellationToken),
            GrainReadKind.ChangeFeed => database.ReadChangeFeed(principal, GrainPayloadJson.Read<ReadChangeFeedRequest>(payload)),
            GrainReadKind.OutboxStatus => database.GetOutboxStatus(principal, GrainPayloadJson.Read<GetOutboxStatusRequest>(payload).Partition),
            GrainReadKind.ProjectionBatch => database.ReadProjectionBatch(principal, GrainPayloadJson.Read<ReadProjectionBatchRequest>(payload)),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
    }

    internal static bool Handles(GrainReadKind kind) => kind is >= GrainReadKind.Document and <= GrainReadKind.Samples
        or GrainReadKind.ChangeFeed or GrainReadKind.OutboxStatus or GrainReadKind.ProjectionBatch
        or GrainReadKind.LatestSample or GrainReadKind.AggregateSamples or GrainReadKind.AggregateSampleWindows;

    private StreamPage Stream(string principal, ReadStreamRequest request, CancellationToken cancellationToken)
        => database.ReadStream(principal, request.Stream, request.AfterRevision, request.Limit, cancellationToken);

    private MessageInspection? Message(string principal, InspectMessageRequest request)
        => database.InspectMessage(principal, request.Lane, request.Id);

    private GraphTraversal Traverse(string principal, TraverseRequest request, CancellationToken cancellationToken)
        => database.Traverse(principal, request.Partition, request.Graph, request.Start, request.MaxDepth,
            request.MaxVertices, request.MaxEdges, request.Labels?.ToArray(), cancellationToken);

    private SampleRecord[] Samples(string principal, ReadSamplesRequest request, CancellationToken cancellationToken)
        => database.ReadSamples(principal, request.Partition, request.Set, request.SeriesId,
            request.From, request.Until, request.Limit, cancellationToken);
}
