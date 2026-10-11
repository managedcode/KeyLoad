using System.Collections.Immutable;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

internal static class EventVectorControlRefresh
{
    private const int Version = 1;
    private const int FirstOrdinal = 0;
    private const int SinglePage = 1;
    private const long GenerationStep = 1;
    private const string InvalidRefresh = "The original complete event vector refresh is inconsistent.";

    internal static EventVectorMap Execute(IAtomicTransaction transaction, EventVectorControlPhase control,
        IOptions<DatabaseLimits> options, CancellationToken cancellationToken)
    {
        var admission = new EventVectorAdmissionPolicy(options);
        var request = control.OriginalRequest;
        var inventory = EventVectorMapInventory.Read(transaction, request.ControlPartition,
            request.MapId, options, cancellationToken);
        var map = inventory.Header ?? throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidRefresh);
        EventVectorInventoryValidation.Require(map, inventory.PayloadRows, admission);
        if (control.Action != EventVectorControlAction.PublishRefresh || map.PendingPhaseId is not null
            || map.State is not (EventVectorMapState.Refreshing or EventVectorMapState.Active)
            || inventory.PayloadRows.Any(row => row.Key.Span.SequenceEqual(
                EventVectorKeys.Offer(map.ControlPartition, map.MapId))))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidRefresh); }
        var proofRow = ProofRow(map, inventory, control, admission);
        var proof = EventVectorCoverageProofValidation.Read(proofRow, map, admission);
        var prior = EventVectorControlPages.Entries(inventory, admission);
        var candidate = EventVectorRefreshEntries.Create(prior, proof, admission);
        var supplied = EventVectorEntryEncoding.Decode(control.OriginalEncodedEntries,
            control.OriginalEntryChecksum, admission);
        if (!candidate.SequenceEqual(supplied)
            || map.State == EventVectorMapState.Active && (!prior.IsEmpty || !candidate.IsEmpty))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidRefresh); }
        EventVectorRefreshSettlement.Require(inventory, candidate, admission);
        var page = new EventVectorPage
        {
            Version = Version,
            MapId = map.MapId,
            PageOrdinal = FirstOrdinal,
            Entries = control.OriginalEncodedEntries,
            Checksum = control.OriginalEntryChecksum
        };
        var pages = EventVectorKeys.Pages(map.ControlPartition, map.MapId);
        var proofs = EventVectorKeys.Proofs(map.ControlPartition, map.MapId);
        var payload = inventory.PayloadRows.Where(row => !row.Key.Span.StartsWith(pages)
            && !row.Key.Span.StartsWith(proofs)).ToImmutableArray()
            .Add(proofRow).Add(EventVectorEncodedRow.Create(
                EventVectorKeys.Page(map.ControlPartition, map.MapId, FirstOrdinal), page, admission));
        return EventVectorMapWriter.Write(transaction, map with
        {
            State = EventVectorMapState.Active,
            Revision = checked(map.Revision + GenerationStep),
            PageCount = SinglePage,
            CoverageGeneration = checked(map.CoverageGeneration + GenerationStep),
            SourceManifestDigest = proof.OriginalSemanticDigest
        }, payload, options, cancellationToken);
    }

    private static EventVectorEncodedRow ProofRow(EventVectorMap map, EventVectorOwnedInventory inventory,
        EventVectorControlPhase control, EventVectorAdmissionPolicy admission)
    {
        var generation = checked(map.CoverageGeneration + GenerationStep);
        var key = EventVectorKeys.Proof(map.ControlPartition, map.MapId, generation);
        var candidate = EventVectorControlProof.Create(control, generation, admission);
        var row = inventory.PayloadRows.SingleOrDefault(current => current.Key.Span.SequenceEqual(key));
        if (row.Key.IsEmpty)
        {
            if (map.State != EventVectorMapState.Active)
            { throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidRefresh); }
            return EventVectorEncodedRow.Create(key, candidate, admission);
        }
        var original = EventVectorCoverageProofValidation.Read(row, map, admission);
        if (!original.OriginalSemanticProjectionBytes.Span.SequenceEqual(candidate.OriginalSemanticProjectionBytes.Span))
        { throw Errors.Fail(ErrorCode.Conflict, InvalidRefresh); }
        return row;
    }
}
