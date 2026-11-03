using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Query.Search;

namespace KeyLoad.Server.Search;

internal static class TextCandidateReader
{
    private const int FirstRecordOrdinal = 1;
    private const int EmptyPositivesCount = 0;

    internal static void Verify(TextGeneration generation, IReadOnlyList<string> terms,
        IReadOnlyList<EntityRef> positives, ReadExecutionBudget budget, Func<string, ulong> hash,
        Action markInvalid)
    {
        budget.Check();
        var positiveSet = new HashSet<EntityRef>(positives);
        if (positiveSet.Count != positives.Count)
        {
            throw TextErrors.Corrupt();
        }
        budget.Check();
        var candidateSet = ReadOrdinals(generation, terms, budget, hash, proof: null);
        VerifyPositives(generation, positiveSet, candidateSet, markInvalid, budget);
    }

    internal static TextPostingCandidates ReadCertified(TextGeneration generation, IReadOnlyList<string> terms,
        ReadExecutionBudget budget, Func<string, ulong> hash)
    {
        var proof = generation.Corpus.PostingProof;
        var ordinals = ReadOrdinals(generation, terms, budget, hash, proof).ToArray();
        Array.Sort(ordinals);
        var candidates = ImmutableArray.CreateBuilder<TextPostingCandidate>(ordinals.Length);
        foreach (var ordinal in ordinals)
        {
            budget.Check();
            var record = generation.Corpus.Records[checked((int)ordinal - FirstRecordOrdinal)];
            candidates.Add(new(record.Reference, record.Revision));
        }
        return new(proof.DocumentCount, proof.TotalLength, candidates.MoveToImmutable());
    }

    private static HashSet<ulong> ReadOrdinals(TextGeneration generation, IReadOnlyList<string> terms,
        ReadExecutionBudget budget, Func<string, ulong> hash, TextPostingProof? proof)
    {
        var candidates = new HashSet<ulong>();
        var tokenHashes = new HashSet<ulong>();
        foreach (var term in terms)
        {
            budget.Check();
            if (string.IsNullOrEmpty(term))
            {
                continue;
            }
            var token = hash(term);
            if (tokenHashes.Add(token))
            {
                ReadTokenCandidates(generation, token, candidates, budget, proof);
            }
        }
        return candidates;
    }

    private static void ReadTokenCandidates(TextGeneration generation, ulong token,
        HashSet<ulong> candidates, ReadExecutionBudget budget, TextPostingProof? proof)
    {
        const int EmptyPostingsCount = 0;
        using var digest = proof is null ? null : IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var iterator = generation.Index.ZoneTree1.CreateIterator(global::ZoneTree.IteratorType.NoRefresh,
            contributeToTheBlockCache: false);
        budget.Check();
        iterator.Seek(RuntimeTextIndex.LowerBound(token));
        long count = EmptyPostingsCount;
        while (true)
        {
            budget.Check();
            if (!iterator.Next())
            {
                break;
            }
            var key = iterator.CurrentKey;
            if (key.Token != token)
            {
                break;
            }
            if (key.Record < FirstRecordOrdinal || key.Record > (ulong)generation.Corpus.Records.Count
                || iterator.CurrentValue != default)
            {
                throw TextErrors.Corrupt();
            }
            candidates.Add(key.Record);
            if (digest is not null)
            {
                TextPostingProof.AppendPosting(digest, key.Token, key.Record, key.PreviousToken);
                count = checked(count + 1);
            }
        }
        if (digest is not null)
        {
            Span<byte> fingerprint = stackalloc byte[SHA256.HashSizeInBytes];
            if (!digest.TryGetHashAndReset(fingerprint, out var written) || written != fingerprint.Length)
            {
                throw TextErrors.Corrupt();
            }
            proof!.VerifyToken(token, count, fingerprint);
        }
    }

    private static void VerifyPositives(TextGeneration generation, HashSet<EntityRef> positives,
        HashSet<ulong> candidates, Action markInvalid, ReadExecutionBudget budget)
    {
        foreach (var record in generation.Corpus.Records)
        {
            budget.Check();
            if (positives.Remove(record.Reference) && !candidates.Contains(record.Id))
            {
                markInvalid();
                throw TextErrors.Corrupt();
            }
        }
        if (positives.Count != EmptyPositivesCount)
        {
            markInvalid();
            throw TextErrors.Mismatch();
        }
    }
}
