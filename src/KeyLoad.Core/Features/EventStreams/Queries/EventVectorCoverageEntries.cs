namespace KeyLoad.Core;

internal static class EventVectorCoverageEntries
{
    private const long BeforeFirstPosition = 1;
    private const long UnconsumedEventSequence = 0;
    private const string InvalidHead = "The captured native event vector source frontier is inconsistent.";

    internal static EventVectorEntry Create(EventSourceRef source, EventSourceHead head,
        ResourceDefinition resource, AtomicPartitionPlacementResolution placement,
        PrincipalRecord principal, EventFeedStartPolicy start, long actualAppliedCut)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(head);
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(principal);
        if (!Enum.IsDefined(start) || head.Generation != source.Generation
            || head.FirstAvailablePosition < BeforeFirstPosition || head.TailPosition < UnconsumedEventSequence
            || head.FirstAvailablePosition > checked(head.TailPosition + BeforeFirstPosition)
            || placement.Partition != source.Partition || actualAppliedCut < UnconsumedEventSequence)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidHead); }
        return new EventVectorEntry
        {
            Source = source,
            Position = start == EventFeedStartPolicy.FromNow ? head.TailPosition
                : checked(head.FirstAvailablePosition - BeforeFirstPosition),
            EventSequence = UnconsumedEventSequence,
            CapturedCut = actualAppliedCut,
            Incarnation = placement.Incarnation,
            PlacementEpoch = placement.PlacementEpoch,
            PrincipalId = principal.Id,
            PolicyEpoch = principal.PolicyEpoch,
            SchemaVersion = resource.SchemaVersion,
            LogicalPlacementRevision = placement.Revision,
            DirectoryFence = placement.DirectoryRevision,
            FirstAvailablePosition = head.FirstAvailablePosition,
            CapturedTailPosition = head.TailPosition,
        };
    }
}
