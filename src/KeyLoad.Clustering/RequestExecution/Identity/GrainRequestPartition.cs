using KeyLoad.Core.BlobStorage;
using KeyLoad.Query;

namespace KeyLoad.Clustering;

/// <summary>Routes each single-partition operation by its typed scope; multi-partition capabilities use a database cut vector.</summary>
internal static class GrainRequestPartition
{
    internal static (bool HasValue, PartitionRef? Value) Read(DecodedGrainRequest request)
    {
        if (request.Envelope.ReadKind == GrainReadKind.Query)
        {
            return Single((GrainPayload.Read<QueryRequest>((request.Payload /* KLD0041: Decode the verified Query slot as the selected QueryRequest. */)) /* KLD0041: Validate the selected SQL payload before reading its canonical partition. */).Partition);
        }
        if (request.Envelope.ReadKind == GrainReadKind.AstQuery)
        {
            return Single((GrainPayload.ReadPublicInput<AstQueryRequest>((request.Payload /* KLD0041: Decode the verified AstQuery slot as the selected AstQueryRequest. */)) /* KLD0041: Validate the selected query AST payload before reading its canonical partition. */).Partition);
        }
        if (request.Envelope.ReadKind == GrainReadKind.Search)
        {
            return Single((GrainPayload.Read<SearchRequest>((request.Payload /* KLD0041: Decode the verified Search envelope slot as its selected SearchRequest. */)) /* KLD0041: Validate the exact Search payload before selecting its canonical partition owner. */).Partition);
        }
        if (request.Envelope.ReadKind == GrainReadKind.ApproximateSearch)
        {
            return Single((GrainPayload.Read<ApproximateSearchRequest>((request.Payload /* KLD0041: Decode the verified ApproximateSearch envelope slot as its selected ApproximateSearchRequest. */)) /* KLD0041: Validate the exact ApproximateSearch payload before selecting its canonical partition owner. */).Search.Partition);
        }
        if (request.Envelope.ReadKind == GrainReadKind.GraphSearch)
        {
            return Single((GrainPayload.Read<GraphSearchRequest>((request.Payload /* KLD0041: Decode the verified GraphSearch envelope slot as its selected GraphSearchRequest. */)) /* KLD0041: Validate the exact GraphSearch payload before selecting its canonical partition owner. */).Search.Partition);
        }
        if (request.Envelope.ReadKind == GrainReadKind.WaitForIndex)
        {
            return Single((GrainPayload.Read<WaitForIndexRequest>((request.Payload /* KLD0041: Decode the verified WaitForIndex envelope slot as its selected WaitForIndexRequest. */)) /* KLD0041: Validate the exact WaitForIndex payload before selecting its canonical partition owner. */).Partition);
        }
        if (request.Envelope.ReadKind == GrainReadKind.WaitForAnnIndex)
        {
            return Single((GrainPayload.Read<WaitForAnnIndexRequest>((request.Payload /* KLD0041: Decode the verified WaitForAnnIndex envelope slot as its selected WaitForAnnIndexRequest. */)) /* KLD0041: Validate the exact WaitForAnnIndex payload before selecting its canonical partition owner. */).Partition);
        }
        return ReadOther(request);
    }

    private static (bool HasValue, PartitionRef? Value) ReadOther(DecodedGrainRequest request) => request.Envelope.ReadKind switch
    {
        GrainReadKind.Document => Single(Payload<GetDocumentRequest>(request).Reference?.Partition),
        GrainReadKind.Stream => Single(Payload<ReadStreamRequest>(request).Stream?.Partition),
        GrainReadKind.EventSource => Single(Payload<ReadEventSourceRequest>(request).Source?.Partition),
        GrainReadKind.Subscription => Single(Payload<GetSubscriptionRequest>(request).Subscription?.Source?.Partition),
        GrainReadKind.Message => Single(Payload<InspectMessageRequest>(request).Lane?.Partition),
        GrainReadKind.AggregateReplay => Single(Payload<ReadAggregateReplayRequest>(request).Stream?.Partition),
        GrainReadKind.GraphShortestPath => Single(Payload<GraphShortestPathRequest>(request).Partition),
        GrainReadKind.ChangeFeed => Single(Payload<ReadChangeFeedRequest>(request).Partition),
        GrainReadKind.OutboxStatus => Single(Payload<GetOutboxStatusRequest>(request).Partition),
        GrainReadKind.RecurringSchedule => Single(Payload<InspectRecurringScheduleRequest>(request).Lane?.Partition),
        GrainReadKind.Saga => Single(Payload<InspectSagaRequest>(request).Lane?.Partition),
        GrainReadKind.QueueTransfer => Single(Payload<InspectQueueTransferRequest>(request).SourceQueue?.Partition),
        GrainReadKind.QueueTransferReceipt => Single(Payload<InspectQueueTransferReceiptRequest>(request).DestinationQueue?.Partition),
        GrainReadKind.FileMetadata => Single(Payload<FileMetadataQuery>(request).Address?.Partition),
        GrainReadKind.AnalyticsEvents => Single(Payload<ReadAnalyticsEventsRequest>(request).Partition),
        GrainReadKind.AnalyticsAggregate => Single(Payload<AggregateAnalyticsRequest>(request).Partition),
        GrainReadKind.LiveQueryStart => Single(Payload<StartLiveQueryRequest>(request).Query?.Partition),
        GrainReadKind.LiveQueryRead => Single(Payload<ReadLiveQueryRequest>(request).Query?.Partition),
        GrainReadKind.SqlGraphSearch => Single(Payload<SqlGraphSearchRequest>(request).Query?.Partition),
        GrainReadKind.SqlGraphPath => Single(Payload<SqlGraphPathRequest>(request).Query?.Partition),
        _ => ReadTimeSeries(request)
    };

    private static (bool HasValue, PartitionRef? Value) ReadTimeSeries(DecodedGrainRequest request) => request.Envelope.ReadKind switch
    {
        GrainReadKind.Samples => Single(Payload<ReadSamplesRequest>(request).Partition),
        GrainReadKind.LatestSample => Single(Payload<ReadLatestSampleRequest>(request).Partition),
        GrainReadKind.AggregateSamples => Single(Payload<AggregateSamplesRequest>(request).Partition),
        GrainReadKind.AggregateSampleWindows => Single(Payload<AggregateSampleWindowsRequest>(request).Partition),
        GrainReadKind.SampleRetention => Single(Payload<ReadSampleRetentionRequest>(request).Partition),
        GrainReadKind.SampleRollup => Single(Payload<ReadSampleRollupRequest>(request).Partition),
        GrainReadKind.SampleChunkWindow => Single(Payload<ReadSampleChunkWindowRequest>(request).Partition),
        _ => (false, default)
    };

    private static (bool HasValue, PartitionRef? Value) Single(PartitionRef? partition)
        => (partition is not null, partition);

    private static T Payload<T>(DecodedGrainRequest request)
        => (GrainPayload.ReadPublicInput<T>(request.Payload /* KLD0041: Read the received native wire slot only at its closed read-kind decoding boundary. */) /* KLD0041: Validate this native envelope slot as the exact T selected by the closed read-kind scope table before typed field access. */);
}
