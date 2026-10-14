using System.Collections.Immutable;
using System.Security.Cryptography;

namespace KeyLoad.Core;

internal static class EventVectorControlRefreshAdmission
{
    private const long CoverageStep = 1;
    private const int FirstOrdinal = 0;
    private const string InvalidRefresh = "The original event vector refresh source admission is inconsistent.";

    internal static EventVectorEncodedRow? Require(EventVectorMap map, EventVectorOwnedInventory inventory,
        EventVectorControlPhase control, EventVectorSourcePhase intended, EventVectorAdmissionPolicy admission)
    {
        var nextCoverage = checked(map.CoverageGeneration + CoverageStep);
        var proof = EventVectorControlProof.Create(control, nextCoverage, admission);
        var key = EventVectorKeys.Proof(map.ControlPartition, map.MapId, nextCoverage);
        EventVectorEncodedRow? added = null;
        var existing = inventory.PayloadRows.SingleOrDefault(row => row.Key.Span.SequenceEqual(key));
        if (existing.Key.IsEmpty)
        {
            added = EventVectorEncodedRow.Create(key, proof, admission);
        }
        else
        {
            var original = EventVectorCoverageProofValidation.Read(existing, map, admission);
            if (!CryptographicOperations.FixedTimeEquals(original.OriginalSemanticDigest.Span,
                    proof.OriginalSemanticDigest.Span)
                || !original.OriginalSemanticProjectionBytes.Span.SequenceEqual(proof.OriginalSemanticProjectionBytes.Span))
            {
                throw Errors.Fail(ErrorCode.Conflict, InvalidRefresh);
            }
            proof = original;
        }
        var prior = EventVectorControlPages.Entries(inventory, admission);
        var candidate = EventVectorRefreshEntries.Create(prior, proof, admission);
        if (intended.Role == EventVectorSourcePhaseRole.Release)
        {
            RequireRemoved(map, prior, candidate, intended);
        }
        else
        {
            RequireRetained(map, prior, candidate, intended);
        }
        return added;
    }

    private static void RequireRemoved(EventVectorMap map, ImmutableArray<EventVectorEntry> prior,
        ImmutableArray<EventVectorEntry> candidate, EventVectorSourcePhase intended)
    {
        var ordinal = intended.SourceOrdinal;
        if (ordinal < FirstOrdinal || ordinal >= prior.Length || intended.Source != prior[ordinal].Source
            || candidate.Any(entry => entry.Source == intended.Source)
            || intended.CoverageGeneration != map.CoverageGeneration
            || intended.OriginalPosition != prior[ordinal].Position || intended.TargetPosition != prior[ordinal].Position)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidRefresh);
        }
    }

    private static void RequireRetained(EventVectorMap map, ImmutableArray<EventVectorEntry> prior,
        ImmutableArray<EventVectorEntry> candidate, EventVectorSourcePhase intended)
    {
        var ordinal = intended.SourceOrdinal;
        if (ordinal < FirstOrdinal || ordinal >= candidate.Length || intended.Source != candidate[ordinal].Source
            || intended.CoverageGeneration != checked(map.CoverageGeneration + CoverageStep))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidRefresh);
        }
        var original = prior.FirstOrDefault(entry => entry.Source == intended.Source);
        var role = original is null ? EventVectorSourcePhaseRole.Pin : EventVectorSourcePhaseRole.Advance;
        var before = original?.Position ?? candidate[ordinal].Position;
        if (intended.Role != role || intended.OriginalPosition != before
            || intended.TargetPosition != candidate[ordinal].Position)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidRefresh);
        }
    }
}
