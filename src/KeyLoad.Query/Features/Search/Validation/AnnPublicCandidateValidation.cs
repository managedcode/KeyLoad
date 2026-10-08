using System.Collections.Immutable;
using KeyLoad.Core.Features.Search;

namespace KeyLoad.Query.Features.Search;

internal static class AnnPublicCandidateValidation
{
    private const int Empty = 0;
    private const int Step = 1;
    private const int MidpointDivisor = 2;
    private const int BitmapWordBits = 64;
    private const int BitmapRemainder = 63;
    private const ulong SetBitmapBit = 1UL;
    private const string Invalid = "The native ANN generation is corrupt.";

    internal static SearchScore[] Scores(SearchRequest request, AnnSeed caller,
        ImmutableArray<VectorRecord> source, ReadOnlySpan<ulong> eligible, AnnCandidate[] candidates, AnnWorkBudget work)
    {
        work.Charge(candidates.Length);
        var scores = new SearchScore[candidates.Length];
        for (var index = Empty; index < candidates.Length; index++)
        {
            var candidate = candidates[index];
            ValidateSource(candidate, source, eligible, candidates.AsSpan(Empty, index), work);
            var actual = Find(caller, candidate.DocumentId, work);
            if (candidate.DocumentRevision != actual.DocumentRevision || !double.IsFinite(candidate.Score))
            { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
            scores[index] = new(new(request.Partition, request.Collection, actual.DocumentId), candidate.Score);
        }
        work.Check();
        return scores;
    }

    private static void ValidateSource(AnnCandidate candidate, ImmutableArray<VectorRecord> source,
        ReadOnlySpan<ulong> eligible, ReadOnlySpan<AnnCandidate> preceding, AnnWorkBudget work)
    {
        var ordinal = candidate.SourceOrdinal;
        if (ordinal < Empty || ordinal >= source.Length
            || (eligible[ordinal / BitmapWordBits] & (SetBitmapBit << (ordinal & BitmapRemainder))) == Empty)
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        var expected = source[ordinal];
        work.Charge(checked((long)expected.DocumentId.Length + candidate.DocumentId.Length + Step));
        if (!StringComparer.Ordinal.Equals(expected.DocumentId, candidate.DocumentId)
            || expected.DocumentRevision != candidate.DocumentRevision)
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        foreach (var earlier in preceding)
        {
            work.Charge(Step);
            if (earlier.SourceOrdinal == ordinal)
            { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        }
    }

    private static VectorRecord Find(AnnSeed caller, string id, AnnWorkBudget work)
    {
        var first = Empty;
        var after = caller.Records.Length;
        while (first < after)
        {
            var middle = first + (after - first) / MidpointDivisor;
            var actual = caller.Records[middle];
            work.Charge(checked((long)actual.DocumentId.Length + id.Length + Step));
            var order = StringComparer.Ordinal.Compare(actual.DocumentId, id);
            if (order == Empty)
            { return actual; }
            if (order < Empty)
            { first = middle + Step; }
            else
            { after = middle; }
        }
        throw Errors.Fail(ErrorCode.Corruption, Invalid);
    }
}
