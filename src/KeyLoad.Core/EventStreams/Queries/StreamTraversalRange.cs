using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private (ImmutableArray<EventRecord> Events, bool HasMore) ReadTraversalEvents(IKeyValueView bounded,
        StreamRef stream, PrincipalRecord principal, ResourceDefinition resource, StreamTraversalCursor cursor,
        int limit, ReadExecutionBudget budget)
    {
        var prefix = KeySpace.Partition(EventSpace, stream.Partition, stream.StreamSet, stream.StreamId, stream.Generation);
        var backward = cursor.Direction == StreamReadDirection.Backward;
        var lower = backward ? cursor.CapturedFirstAvailableRevision - StreamTraversalProtocol.FirstRevision : cursor.NextExclusiveRevision;
        var upper = backward ? cursor.NextExclusiveRevision : cursor.CapturedTailRevision + StreamTraversalProtocol.FirstRevision;
        var after = KeySpace.Partition(EventSpace, stream.Partition, stream.StreamSet, stream.StreamId, stream.Generation, lower);
        var until = KeySpace.Partition(EventSpace, stream.Partition, stream.StreamSet, stream.StreamId, stream.Generation, upper);
        var events = new List<EventRecord>();
        var projectedBytes = StreamTraversalProtocol.EmptyRevision;
        var expectedExclusiveRevision = cursor.NextExclusiveRevision;
        bool Visit(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
        {
            var record = NativeSerialization.Deserialize<EventRecord>(value);
            if (record.Stream != stream || record.Revision <= lower || record.Revision >= upper
                || record.Revision != expectedExclusiveRevision + (backward
                    ? -StreamTraversalProtocol.FirstRevision : StreamTraversalProtocol.FirstRevision)
                || !key.SequenceEqual(KeySpace.Partition(EventSpace, stream.Partition,
                    stream.StreamSet, stream.StreamId, stream.Generation, record.Revision)))
            {
                throw Errors.Fail(ErrorCode.Corruption, StreamTraversalProtocol.InvalidRequest);
            }
            var projected = ProjectEvent(principal, resource, record);
            var bytes = budget.MeasureResult(projected);
            if (bytes > budget.MaximumResultBytes - projectedBytes)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, StreamTraversalProtocol.Bytes);
            }
            events.Add(projected);
            projectedBytes += bytes;
            expectedExclusiveRevision = record.Revision;
            return true;
        }
        var result = backward
            ? bounded.VisitReverseRange(prefix, limit, Visit, after, until)
            : bounded.VisitRange(prefix, limit, Visit, after, until);
        if (!result.HasMore && (backward
            ? expectedExclusiveRevision > cursor.CapturedFirstAvailableRevision
            : expectedExclusiveRevision < cursor.CapturedTailRevision))
        {
            throw Errors.Fail(ErrorCode.Corruption, StreamTraversalProtocol.InvalidRequest);
        }
        return (events.ToImmutableArray(), result.HasMore);
    }
}
