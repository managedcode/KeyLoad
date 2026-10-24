using KeyLoad.Core;

namespace KeyLoad.Server.Search;

internal static class TextCandidateReader
{
    internal static void Verify(TextGeneration generation, IReadOnlyList<string> terms,
        IReadOnlyList<EntityRef> positives, ReadExecutionBudget budget, Func<string, ulong> hash,
        Action markInvalid)
    {
        var limit = generation.Limits.MaxScanRecords;
        if (positives.Count > limit || terms.Count > generation.Limits.MaxSearchTextTokens)
        {
            throw TextErrors.BoundExceeded();
        }
        budget.Check();
        var positiveSet = new HashSet<EntityRef>(positives);
        if (positiveSet.Count != positives.Count)
        {
            throw TextErrors.Corrupt();
        }
        budget.Check();
        var candidateSet = new HashSet<EntityRef>();
        budget.Check();
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
                ReadTokenCandidates(generation, token, candidateSet, budget, limit);
            }
        }
        VerifyPositives(generation, positiveSet, candidateSet, markInvalid, budget);
    }

    private static void ReadTokenCandidates(TextGeneration generation, ulong token,
        HashSet<EntityRef> candidates, ReadExecutionBudget budget, int maximumCandidates)
    {
        using var iterator = generation.Index.ZoneTree1.CreateIterator(global::ZoneTree.IteratorType.NoRefresh,
            contributeToTheBlockCache: false);
        budget.Check();
        iterator.Seek(RuntimeTextIndex.LowerBound(token));
        while (true)
        {
            budget.ChargeBytes(TextProtocol.PostingBytes);
            if (!iterator.Next())
            {
                return;
            }
            var key = iterator.CurrentKey;
            if (key.Token != token)
            {
                return;
            }
            AddCandidate(generation, key.Record, candidates, maximumCandidates);
        }
    }

    private static void AddCandidate(TextGeneration generation, ulong id,
        HashSet<EntityRef> candidates, int maximumCandidates)
    {
        const int IdEmptyCount = 0;
        const int RecordsSecondIndex = 1;

        if (id is IdEmptyCount || id > (ulong)generation.Records.Count)
        {
            throw TextErrors.Corrupt();
        }
        candidates.Add(generation.Records[checked((int)id - RecordsSecondIndex)].Reference);
        if (candidates.Count > maximumCandidates)
        {
            throw TextErrors.BoundExceeded();
        }
    }

    private static void VerifyPositives(TextGeneration generation, HashSet<EntityRef> positives,
        HashSet<EntityRef> candidates, Action markInvalid, ReadExecutionBudget budget)
    {
        const int EmptyPositivesCount = 0;

        foreach (var record in generation.Records)
        {
            budget.Check();
            if (positives.Remove(record.Reference) && !candidates.Contains(record.Reference))
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
