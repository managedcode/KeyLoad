using System.Collections.Immutable;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

internal static class EventVectorControlPublishOpen
{
    private const int FirstOrdinal = 0;
    private const string IncompleteOpen = "The original event vector open has unsettled source ownership.";

    internal static EventVectorMap Execute(IAtomicTransaction transaction, EventVectorControlPhase phase,
        IOptions<DatabaseLimits> options, CancellationToken cancellationToken)
    {
        var admission = new EventVectorAdmissionPolicy(options);
        var request = phase.OriginalRequest;
        var inventory = EventVectorMapInventory.Read(transaction, request.ControlPartition,
            request.MapId, options, cancellationToken);
        var map = inventory.Header ?? throw Errors.Fail(ErrorCode.RecoveryRequired, IncompleteOpen);
        EventVectorInventoryValidation.Require(map, inventory.PayloadRows, admission);
        if (phase.Action != EventVectorControlAction.PublishOpen || map.State != EventVectorMapState.Opening
            || map.PendingPhaseId is not null || map.OriginalOpenCommandId != request.CommandId)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, IncompleteOpen);
        }
        var entries = EventVectorEntryEncoding.Decode(phase.OriginalEncodedEntries,
            phase.OriginalEntryChecksum, admission);
        var proof = EventVectorControlPublication.Proof(inventory, admission);
        if (!proof.OriginalEncodedEntries.Span.SequenceEqual(phase.OriginalEncodedEntries.Span))
        {
            throw Errors.Fail(ErrorCode.Corruption, IncompleteOpen);
        }
        var observed = EventVectorControlPublication.Phases(inventory, EventVectorSourcePhaseRole.Pin, admission);
        RequireComplete(map, entries, observed, admission);
        return EventVectorMapWriter.Write(transaction, map with
        {
            State = EventVectorMapState.Active
        },
            inventory.PayloadRows, options, cancellationToken);
    }

    private static void RequireComplete(EventVectorMap map, ImmutableArray<EventVectorEntry> entries,
        Dictionary<int, EventVectorSourcePhase> observed, EventVectorAdmissionPolicy admission)
    {
        if (observed.Count != entries.Length)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, IncompleteOpen);
        }
        for (var ordinal = FirstOrdinal; ordinal < entries.Length; ordinal++)
        {
            if (!observed.TryGetValue(ordinal, out var phase)
                || phase.Source != entries[ordinal].Source || phase.TargetPosition != entries[ordinal].Position
                || phase.Disposition != EventVectorSourcePhaseDisposition.Observed
                || phase.SourceOriginalResult?.Error is not null || phase.SourceOriginalResult is null)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, IncompleteOpen);
            }
            var request = EventVectorSourceMutationValidation.ReadObserved(phase, admission);
            EventVectorObservedPinValidation.Require(phase, request,
                phase.SourceOriginalResult.Get<EventVectorSourceResult>(), admission);
            if (request.CoverageGeneration != map.CoverageGeneration)
            {
                throw Errors.Fail(ErrorCode.Corruption, IncompleteOpen);
            }
        }
    }
}
