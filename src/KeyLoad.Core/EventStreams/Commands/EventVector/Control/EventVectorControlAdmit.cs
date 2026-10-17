using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

internal static class EventVectorControlAdmit
{
    private const int FirstOrdinal = 0;
    private const string InvalidAdmission = "The original event vector source admission is inconsistent.";

    internal static EventVectorSourcePhase Execute(IAtomicTransaction transaction, EventVectorControlPhase control,
        IOptions<DatabaseLimits> options, CancellationToken cancellationToken)
    {
        var admission = new EventVectorAdmissionPolicy(options);
        var request = control.OriginalRequest;
        var inventory = EventVectorMapInventory.Read(transaction, request.ControlPartition,
            request.MapId, options, cancellationToken);
        var map = inventory.Header ?? throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidAdmission);
        EventVectorInventoryValidation.Require(map, inventory.PayloadRows, admission);
        var intended = control.IntendedSourcePhase;
        if (control.Action != EventVectorControlAction.AdmitSource || intended is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidAdmission);
        }
        if (!control.OriginalCleanupFrontierBytes.IsEmpty)
        {
            if (map.State != EventVectorMapState.Releasing || request.Action != EventFeedControlAction.Release)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidAdmission);
            }
            EventVectorCleanupSourceBinding.Require(control, intended, admission);
            return EventVectorControlPhaseWriter.Admit(transaction, intended, options,
                cancellationToken, EventVectorMapState.Releasing);
        }
        var state = State(request.Action);
        if (map.State != state && map.State != EventVectorMapState.Active
            || request.Action == EventFeedControlAction.Open && map.State != EventVectorMapState.Opening)
        {
            throw Errors.Fail(ErrorCode.Conflict, InvalidAdmission);
        }
        if (request.Action == EventFeedControlAction.Refresh && inventory.PayloadRows.Any(row =>
                row.Key.Span.SequenceEqual(EventVectorKeys.Offer(map.ControlPartition, map.MapId))))
        {
            throw Errors.Fail(ErrorCode.Conflict, InvalidAdmission);
        }
        EventVectorEncodedRow? candidateProof = null;
        if (request.Action == EventFeedControlAction.Acknowledge)
        {
            RequireAcknowledgement(map, inventory, intended, admission);
        }
        else if (request.Action == EventFeedControlAction.Release)
        {
            RequireRelease(inventory, intended, admission);
        }
        else if (request.Action == EventFeedControlAction.Refresh)
        {
            candidateProof = EventVectorControlRefreshAdmission.Require(map, inventory, control, intended, admission);
        }
        if (map.State == EventVectorMapState.Active && intended.SourceOrdinal != FirstOrdinal)
        {
            throw Errors.Fail(ErrorCode.Conflict, InvalidAdmission);
        }
        return EventVectorControlPhaseWriter.Admit(transaction, intended, options,
            cancellationToken, state, candidateProof);
    }

    private static EventVectorMapState State(EventFeedControlAction action) => action switch
    {
        EventFeedControlAction.Open => EventVectorMapState.Opening,
        EventFeedControlAction.Acknowledge => EventVectorMapState.Acknowledging,
        EventFeedControlAction.Refresh => EventVectorMapState.Refreshing,
        EventFeedControlAction.Release => EventVectorMapState.Releasing,
        _ => throw Errors.Fail(ErrorCode.Corruption, InvalidAdmission)
    };

    private static void RequireAcknowledgement(EventVectorMap map, EventVectorOwnedInventory inventory,
        EventVectorSourcePhase intended, EventVectorAdmissionPolicy admission)
    {
        var key = EventVectorKeys.Offer(map.ControlPartition, map.MapId);
        var row = inventory.PayloadRows.SingleOrDefault(current => current.Key.Span.SequenceEqual(key));
        if (row.Key.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidAdmission);
        }
        var offer = NativeSerialization.Deserialize<EventVectorOffer>(row.Value.Span);
        var offered = EventVectorEntryEncoding.Decode(offer.Entries, offer.PageDigest, admission);
        var original = EventVectorControlPages.Entries(inventory, admission);
        var ordinal = intended.SourceOrdinal;
        if (ordinal < FirstOrdinal || ordinal >= original.Length || offered.Length != original.Length
            || intended.Role != EventVectorSourcePhaseRole.Advance || intended.Source != original[ordinal].Source
            || intended.Source != offered[ordinal].Source || intended.OriginalPosition != original[ordinal].Position
            || intended.TargetPosition != offered[ordinal].Position)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidAdmission);
        }
    }

    private static void RequireRelease(EventVectorOwnedInventory inventory,
        EventVectorSourcePhase intended, EventVectorAdmissionPolicy admission)
    {
        var entries = EventVectorControlPages.Entries(inventory, admission);
        var ordinal = intended.SourceOrdinal;
        if (ordinal < FirstOrdinal || ordinal >= entries.Length || intended.Role != EventVectorSourcePhaseRole.Release
            || intended.Source != entries[ordinal].Source || intended.OriginalPosition != entries[ordinal].Position
            || intended.TargetPosition != entries[ordinal].Position)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidAdmission);
        }
    }
}
