using System.Collections.Immutable;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

internal static class EventVectorControlAcknowledgement
{
    private const int Version = 1;
    private const int FirstOrdinal = 0;
    private const int SinglePage = 1;
    private const long RevisionStep = 1;
    private const string IncompleteAcknowledgement = "The original event vector acknowledgement has unsettled source ownership.";

    internal static EventVectorMap Execute(IAtomicTransaction transaction, EventVectorControlPhase phase,
        IOptions<DatabaseLimits> options, CancellationToken cancellationToken)
    {
        var admission = new EventVectorAdmissionPolicy(options);
        var request = phase.OriginalRequest;
        var inventory = EventVectorMapInventory.Read(transaction, request.ControlPartition,
            request.MapId, options, cancellationToken);
        var map = inventory.Header ?? throw Errors.Fail(ErrorCode.RecoveryRequired, IncompleteAcknowledgement);
        EventVectorInventoryValidation.Require(map, inventory.PayloadRows, admission);
        if (phase.Action != EventVectorControlAction.PublishAcknowledgement
            || map.State is not (EventVectorMapState.Acknowledging or EventVectorMapState.Active)
            || map.PendingPhaseId is not null)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, IncompleteAcknowledgement);
        }
        var offerKey = EventVectorKeys.Offer(map.ControlPartition, map.MapId);
        var offer = ReadOffer(inventory, offerKey);
        if (!offer.Entries.Span.SequenceEqual(phase.OriginalEncodedEntries.Span)
            || !offer.PageDigest.Span.SequenceEqual(phase.OriginalEntryChecksum.Span))
        {
            throw Errors.Fail(ErrorCode.Corruption, IncompleteAcknowledgement);
        }
        var entries = EventVectorEntryEncoding.Decode(offer.Entries, offer.PageDigest, admission);
        if (map.State == EventVectorMapState.Active && !entries.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, IncompleteAcknowledgement);
        }
        var observed = EventVectorControlPublication.Phases(inventory, EventVectorSourcePhaseRole.Advance, admission);
        RequireComplete(map, entries, observed, admission);
        var page = new EventVectorPage
        {
            Version = Version,
            MapId = map.MapId,
            PageOrdinal = FirstOrdinal,
            Entries = offer.Entries,
            Checksum = offer.PageDigest
        };
        var pageRow = EventVectorEncodedRow.Create(
            EventVectorKeys.Page(map.ControlPartition, map.MapId, FirstOrdinal), page, admission);
        var pagePrefix = EventVectorKeys.Pages(map.ControlPartition, map.MapId);
        var payload = inventory.PayloadRows.Where(row => !row.Key.Span.StartsWith(pagePrefix)
            && !row.Key.Span.SequenceEqual(offerKey)).ToImmutableArray().Add(pageRow);
        return EventVectorMapWriter.Write(transaction, map with
        {
            State = EventVectorMapState.Active,
            Revision = checked(map.Revision + RevisionStep),
            PageCount = SinglePage
        },
            payload, options, cancellationToken);
    }

    private static EventVectorOffer ReadOffer(EventVectorOwnedInventory inventory, byte[] key)
    {
        foreach (var row in inventory.PayloadRows)
        {
            if (row.Key.Span.SequenceEqual(key))
            {
                return NativeSerialization.Deserialize<EventVectorOffer>(row.Value.Span);
            }
        }
        throw Errors.Fail(ErrorCode.RecoveryRequired, IncompleteAcknowledgement);
    }

    private static void RequireComplete(EventVectorMap map, ImmutableArray<EventVectorEntry> entries,
        Dictionary<int, EventVectorSourcePhase> observed, EventVectorAdmissionPolicy admission)
    {
        if (observed.Count != entries.Length)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, IncompleteAcknowledgement);
        }
        for (var ordinal = FirstOrdinal; ordinal < entries.Length; ordinal++)
        {
            if (!observed.TryGetValue(ordinal, out var phase)
                || phase.Source != entries[ordinal].Source || phase.TargetPosition != entries[ordinal].Position
                || phase.Disposition != EventVectorSourcePhaseDisposition.Observed
                || phase.SourceOriginalResult?.Error is not null || phase.SourceOriginalResult is null)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, IncompleteAcknowledgement);
            }
            var original = EventVectorSourceMutationValidation.ReadObserved(phase, admission);
            EventVectorObservedPinValidation.Require(phase, original,
                phase.SourceOriginalResult.Get<EventVectorSourceResult>(), admission);
            if (original.CoverageGeneration != map.CoverageGeneration)
            {
                throw Errors.Fail(ErrorCode.Corruption, IncompleteAcknowledgement);
            }
        }
    }
}
