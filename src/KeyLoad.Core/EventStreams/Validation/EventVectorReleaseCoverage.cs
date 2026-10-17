using System.Collections.Immutable;

namespace KeyLoad.Core;

internal static class EventVectorReleaseCoverage
{
    private const int FirstOrdinal = 0;
    private const string InvalidCoverage = "The complete event vector release coverage is inconsistent.";

    internal static void Require(EventVectorMap map, EventVectorEncodedRow originalProofRow,
        ImmutableArray<EventVectorEntry> currentEntries, EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(admission);
        var proof = EventVectorCoverageProofValidation.Read(originalProofRow, map, admission);
        if (proof.CoverageGeneration != map.CoverageGeneration
            || !proof.OriginalSemanticDigest.Span.SequenceEqual(map.SourceManifestDigest.Span))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidCoverage);
        }
        var captured = EventVectorEntryEncoding.Decode(proof.OriginalEncodedEntries, proof.EntryChecksum, admission);
        if (currentEntries.IsDefault || captured.Length != currentEntries.Length)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidCoverage);
        }
        for (var ordinal = FirstOrdinal; ordinal < captured.Length; ordinal++)
        {
            if (captured[ordinal] is null || currentEntries[ordinal] is null
                || captured[ordinal].Source != currentEntries[ordinal].Source)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidCoverage);
            }
        }
    }
}
