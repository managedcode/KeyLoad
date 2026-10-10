namespace KeyLoad.Core;

internal static class EventVectorCoverageProofInventory
{
    private const string InvalidProof = "The complete native event vector coverage proof inventory is inconsistent.";

    internal static void Add(EventVectorMap header, EventVectorEncodedRow row,
        Dictionary<long, EventVectorCoverageProof> proofs, EventVectorAdmissionPolicy admission, long maximumCoverage)
    {
        var proof = EventVectorCoverageProofValidation.Read(row, header, admission);
        var candidate = proof.CoverageGeneration == maximumCoverage;
        if (proof.CoverageGeneration != header.CoverageGeneration && !candidate
            || !proofs.TryAdd(proof.CoverageGeneration, proof))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidProof); }
    }

    internal static void Require(EventVectorMap header, Dictionary<long, EventVectorCoverageProof> proofs,
        long maximumCoverage)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(proofs);
        if (!proofs.TryGetValue(header.CoverageGeneration, out var active)
            || !active.OriginalSemanticDigest.Span.SequenceEqual(header.SourceManifestDigest.Span))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidProof); }
        foreach (var generation in proofs.Keys)
        {
            if (generation != header.CoverageGeneration && generation != maximumCoverage)
            { throw Errors.Fail(ErrorCode.Corruption, InvalidProof); }
        }
    }
}
