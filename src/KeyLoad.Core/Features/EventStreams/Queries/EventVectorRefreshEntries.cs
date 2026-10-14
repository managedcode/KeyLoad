using System.Collections.Immutable;

namespace KeyLoad.Core;

internal static class EventVectorRefreshEntries
{
    private const int FirstOrdinal = 0;
    private const long PositionStep = 1;
    private const string InvalidRefresh = "The original event vector refresh frontier is inconsistent.";

    internal static ImmutableArray<EventVectorEntry> Create(ImmutableArray<EventVectorEntry> acknowledged,
        EventVectorCoverageProof captured, EventVectorAdmissionPolicy admission)
    {
        var coverage = EventVectorEntryEncoding.Decode(captured.OriginalEncodedEntries,
            captured.EntryChecksum, admission);
        if (acknowledged.IsDefault || acknowledged.Select(static entry => entry.Source).Distinct().Count() != acknowledged.Length
            || coverage.Select(static entry => entry.Source).Distinct().Count() != coverage.Length)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidRefresh);
        }
        var prior = acknowledged.ToDictionary(static entry => entry.Source);
        var result = ImmutableArray.CreateBuilder<EventVectorEntry>(coverage.Length);
        for (var ordinal = FirstOrdinal; ordinal < coverage.Length; ordinal++)
        {
            var current = coverage[ordinal];
            if (!prior.TryGetValue(current.Source, out var original))
            {
                result.Add(current);
                continue;
            }
            RequireAuthority(original, current);
            if (current.CapturedCut < original.CapturedCut || current.CapturedTailPosition < original.Position)
            {
                throw Errors.Fail(ErrorCode.Corruption, InvalidRefresh);
            }
            if (current.FirstAvailablePosition > checked(original.Position + PositionStep))
            {
                throw Errors.Fail(ErrorCode.HistoryUnavailable, InvalidRefresh);
            }
            result.Add(current with
            {
                Position = original.Position,
                EventSequence = original.EventSequence
            });
        }
        return result.MoveToImmutable();
    }

    private static void RequireAuthority(EventVectorEntry original, EventVectorEntry current)
    {
        if (current.Incarnation != original.Incarnation || current.PlacementEpoch != original.PlacementEpoch
            || current.PrincipalId != original.PrincipalId || current.PolicyEpoch != original.PolicyEpoch
            || current.SchemaVersion != original.SchemaVersion
            || current.LogicalPlacementRevision != original.LogicalPlacementRevision
            || current.DirectoryFence != original.DirectoryFence)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidRefresh);
        }
    }
}
