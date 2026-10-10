using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void EventVectorCoveragePartition(IKeyValueView bounded, PrincipalRecord principal,
        EventFeedControlRequest request, PartitionRef partition, PhysicalShardRecord sourceOwner,
        ReadExecutionBudget work, long appliedCut,
        ImmutableArray<EventVectorEntry>.Builder entries,
        ImmutableArray<EventVectorCoverageRow>.Builder headRows, CancellationToken cancellationToken)
    {
        work.Check();
        var scope = request.Scope;
        Authorization.Require(principal, partition, scope.Resource, Capability.SubscriptionsManage);
        Authorization.Require(principal, partition, scope.Resource,
            scope.Kind == EventSourceKind.Topic ? Capability.TopicsRead : Capability.EventsRead);
        if (scope.Kind == EventSourceKind.Topic)
        {
            var captured = ReadEventVectorTopicCapture(bounded, principal.Id, request.ControlPartition,
                partition, scope.Resource, sourceOwner, work, cancellationToken);
            if (captured.Head is not { } head)
            { return; }
            var source = new EventSourceRef(partition, scope.Resource, EventSourceKind.Topic,
                Generation: head.Generation);
            RequireEventVectorCapturedSourceCount(entries.Count);
            entries.Add(EventVectorCoverageEntries.Create(source,
                new EventSourceHead(head.TailPosition, head.FirstAvailablePosition, head.Generation),
                captured.Resource, captured.Placement, principal, request.Start, appliedCut));
            headRows.Add(captured.HeadRow ?? throw Errors.Fail(ErrorCode.Corruption,
                MissingEventVectorCoverageHead));
            return;
        }
        var streams = ReadEventVectorStreamSetCapture(bounded, principal.Id, request.ControlPartition,
            partition, scope.Resource, sourceOwner, work, cancellationToken);
        foreach (var captured in streams.Heads)
        {
            work.Check();
            var head = captured.Head;
            var source = new EventSourceRef(partition, scope.Resource, EventSourceKind.Stream,
                captured.StreamId, head.Generation);
            RequireEventVectorCapturedSourceCount(entries.Count);
            entries.Add(EventVectorCoverageEntries.Create(source,
                new EventSourceHead(head.TailRevision, head.FirstAvailableRevision, head.Generation),
                streams.Resource, streams.Placement, principal, request.Start, appliedCut));
            headRows.Add(captured.OriginalRow);
        }
    }

    private void RequireEventVectorCapturedSourceCount(int currentCount)
    {
        const int AddedCapturedSource = 1;
        if (checked(currentCount + AddedCapturedSource) > Limits.MaxResults)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, MissingEventVectorCoverageHead); }
    }

    private const string MissingEventVectorCoverageHead = "The actual native event vector source head is missing.";
}
