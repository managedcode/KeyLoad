using KeyLoad.Core;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextCandidateReader
{
    internal static void Verify(NativeTextGeneration generation, IReadOnlyList<string> terms,
        IReadOnlyList<EntityRef> positives, ReadExecutionBudget budget, Func<string, ulong> hash,
        Action markInvalid)
    {
        var limit = generation.Limits.MaxScanRecords;
        if (positives.Count > limit || terms.Count > generation.Limits.MaxSearchTextTokens)
        {
            throw NativeTextErrors.BoundExceeded();
        }
        budget.Check();
        var positiveSet = new HashSet<EntityRef>(positives);
        if (positiveSet.Count != positives.Count)
        {
            throw NativeTextErrors.Corrupt();
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

    private static void ReadTokenCandidates(NativeTextGeneration generation, ulong token,
        HashSet<EntityRef> candidates, ReadExecutionBudget budget, int maximumCandidates)
    {
        using var iterator = generation.Index.ZoneTree1.CreateIterator(global::ZoneTree.IteratorType.NoRefresh,
            contributeToTheBlockCache: false);
        budget.Check();
        iterator.Seek(NativeTextIndex.LowerBound(token));
        while (true)
        {
            budget.ChargeBytes(NativeTextProtocol.PostingBytes);
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

    private static void AddCandidate(NativeTextGeneration generation, ulong id,
        HashSet<EntityRef> candidates, int maximumCandidates)
    {
        if (id is 0 || id > (ulong)generation.Records.Count)
        {
            throw NativeTextErrors.Corrupt();
        }
        candidates.Add(generation.Records[checked((int)id - 1)].Reference);
        if (candidates.Count > maximumCandidates)
        {
            throw NativeTextErrors.BoundExceeded();
        }
    }

    private static void VerifyPositives(NativeTextGeneration generation, HashSet<EntityRef> positives,
        HashSet<EntityRef> candidates, Action markInvalid, ReadExecutionBudget budget)
    {
        foreach (var record in generation.Records)
        {
            budget.Check();
            if (positives.Remove(record.Reference) && !candidates.Contains(record.Reference))
            {
                markInvalid();
                throw NativeTextErrors.Corrupt();
            }
        }
        if (positives.Count != 0)
        {
            markInvalid();
            throw NativeTextErrors.Mismatch();
        }
    }
}
