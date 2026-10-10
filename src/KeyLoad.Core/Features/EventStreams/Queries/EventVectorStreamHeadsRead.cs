using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class EventVectorStreamHeadsRead
{
    private const int HeadComponents = 7;
    private const int StreamComponent = 6;
    private const int IncludedSource = 1;
    private const string InvalidHead = "The complete native stream-head coverage is inconsistent.";

    // The owning coverage query has already authorized the scope and StreamSet resource.
    internal static ImmutableArray<EventVectorStreamHeadCapture> Read(IKeyValueView view,
        PartitionRef partition, string streamSet, EventVectorInventoryReadBudget budget,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(budget);
        var heads = new Dictionary<string, EventVectorStreamHeadCapture>(StringComparer.Ordinal);
        var scan = view.VisitRange(KeySpace.Partition(PartitionRecordFamilies.StreamHead, partition, streamSet),
            budget.ScanRecords, (key, value) =>
            {
                var parts = KeyCodec.Decode(key);
                if (parts.Length != HeadComponents || parts[StreamComponent] is not string streamId
                    || !key.SequenceEqual(KeySpace.Partition(PartitionRecordFamilies.StreamHead,
                        partition, streamSet, streamId)))
                { throw Errors.Fail(ErrorCode.Corruption, InvalidHead); }
                budget.RequireSourceCount(checked(heads.Count + IncludedSource));
                var head = NativeSerialization.Deserialize<StreamHead>(value);
                if (head is null || !heads.TryAdd(streamId, new(streamId, head,
                        new EventVectorCoverageRow { Key = key.ToArray(), Value = value.ToArray() })))
                { throw Errors.Fail(ErrorCode.Corruption, InvalidHead); }
                return true;
            }, observer: budget.Observe, cancellationToken: cancellationToken);
        RequireComplete(scan.HasMore, scan.StoppedByVisitor);
        RequireNoOrphans(view, partition, streamSet, PartitionRecordFamilies.Event, heads,
            budget, cancellationToken);
        RequireNoOrphans(view, partition, streamSet, PartitionRecordFamilies.EventIdentity, heads,
            budget, cancellationToken);
        return heads.Values.OrderBy(static item => item.StreamId, StringComparer.Ordinal).ToImmutableArray();
    }

    private static void RequireNoOrphans(IKeyValueView view, PartitionRef partition, string streamSet,
        string family, Dictionary<string, EventVectorStreamHeadCapture> heads,
        EventVectorInventoryReadBudget budget, CancellationToken cancellationToken)
    {
        var scan = view.VisitRange(KeySpace.Partition(family, partition, streamSet), budget.ScanRecords,
            (key, _) =>
            {
                var parts = KeyCodec.Decode(key);
                if (parts.Length <= StreamComponent || parts[StreamComponent] is not string streamId
                    || !heads.ContainsKey(streamId))
                { throw Errors.Fail(ErrorCode.Corruption, InvalidHead); }
                return true;
            }, observer: budget.Observe, cancellationToken: cancellationToken);
        RequireComplete(scan.HasMore, scan.StoppedByVisitor);
    }

    private static void RequireComplete(bool hasMore, bool stopped)
    {
        if (hasMore || stopped)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, InvalidHead); }
    }
}
