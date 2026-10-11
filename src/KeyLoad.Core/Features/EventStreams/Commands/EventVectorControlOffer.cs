using System.Collections.Immutable;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

internal static class EventVectorControlOffer
{
    private const int Version = 1;
    private const string InvalidOffer = "The original complete event vector offer is inconsistent.";

    internal static EventVectorOffer Execute(IAtomicTransaction transaction, EventVectorControlPhase phase,
        DateTimeOffset now, IOptions<DatabaseLimits> options, CancellationToken cancellationToken)
    {
        var admission = new EventVectorAdmissionPolicy(options);
        var request = phase.OriginalRequest;
        var inventory = EventVectorMapInventory.Read(transaction, request.ControlPartition,
            request.MapId, options, cancellationToken);
        var map = inventory.Header ?? throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidOffer);
        EventVectorInventoryValidation.Require(map, inventory.PayloadRows, admission);
        var key = EventVectorKeys.Offer(map.ControlPartition, map.MapId);
        if (phase.Action != EventVectorControlAction.Offer || map.State != EventVectorMapState.Active
            || map.PendingPhaseId is not null || map.ExpiresAt <= now
            || inventory.PayloadRows.Any(row => row.Key.Span.SequenceEqual(key)))
        { throw Errors.Fail(ErrorCode.Conflict, InvalidOffer); }
        var offered = EventVectorEntryEncoding.Decode(phase.OriginalEncodedEntries,
            phase.OriginalEntryChecksum, admission);
        var acknowledged = EventVectorControlPages.Entries(inventory, admission);
        RequireFrontiers(acknowledged, offered);
        EventVectorOfferPageProof.Require(phase, acknowledged, offered, admission);
        var witness = EventVectorCoverageWitnessEncoding.Read(phase.OriginalCoverageWitness, request, admission);
        if (!witness.OriginalSemanticDigest.Span.SequenceEqual(map.SourceManifestDigest.Span))
        { throw Errors.Fail(ErrorCode.Conflict, InvalidOffer); }
        var offer = new EventVectorOffer
        {
            Version = Version,
            MapId = map.MapId,
            MapRevision = map.Revision,
            PageDigest = phase.OriginalEntryChecksum,
            OriginalReadCommandId = request.CommandId,
            Entries = phase.OriginalEncodedEntries,
            IssuedAt = now,
            ExpiresAt = map.ExpiresAt
        };
        var row = EventVectorEncodedRow.Create(key, offer, admission);
        EventVectorMapWriter.Write(transaction, map, inventory.PayloadRows.Add(row), options, cancellationToken);
        return offer;
    }

    private static void RequireFrontiers(ImmutableArray<EventVectorEntry> original,
        ImmutableArray<EventVectorEntry> offered)
    {
        if (original.Length != offered.Length)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidOffer); }
        for (var ordinal = EventVectorControlPages.FirstOrdinal; ordinal < original.Length; ordinal++)
        {
            var before = original[ordinal];
            var after = offered[ordinal];
            if (after.Source != before.Source || after.Position < before.Position
                || after.EventSequence < before.EventSequence || after.Position > after.CapturedTailPosition
                || after.CapturedCut < before.CapturedCut || after.Incarnation != before.Incarnation
                || after.PlacementEpoch != before.PlacementEpoch || after.PrincipalId != before.PrincipalId
                || after.PolicyEpoch != before.PolicyEpoch || after.SchemaVersion != before.SchemaVersion
                || after.LogicalPlacementRevision != before.LogicalPlacementRevision
                || after.DirectoryFence != before.DirectoryFence)
            { throw Errors.Fail(ErrorCode.Corruption, InvalidOffer); }
        }
    }
}
