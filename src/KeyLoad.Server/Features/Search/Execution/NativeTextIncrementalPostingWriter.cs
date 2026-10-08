using KeyLoad.Core;
using ZoneTree.FullTextSearch.Index;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextIncrementalPostingWriter
{
    private const ulong InvalidRecord = 0;

    internal static void Apply(IndexOfTokenRecordPreviousToken<ulong, ulong> index,
        NativeTextIncrementalPosting[] removals, NativeTextIncrementalPosting[] additions,
        ReadExecutionBudget budget, Action? firstPosting = null)
    {
        Require(removals, budget);
        Require(additions, budget);
        foreach (var posting in removals)
        {
            budget.Check();
            index.DeleteRecord(posting.Token, posting.Record, posting.PreviousToken);
            firstPosting?.Invoke();
            firstPosting = null;
        }
        foreach (var posting in additions)
        {
            budget.Check();
            index.UpsertRecord(posting.Token, posting.Record, posting.PreviousToken);
            firstPosting?.Invoke();
            firstPosting = null;
        }
        budget.Check();
    }

    private static void Require(NativeTextIncrementalPosting[] postings, ReadExecutionBudget budget)
    {
        if (postings is null)
        { throw NativeTextErrors.Corrupt(); }
        foreach (var posting in postings)
        {
            budget.ChargeBytes(NativeTextProtocol.PostingBytes);
            if (posting is null || posting.Record == InvalidRecord)
            { throw NativeTextErrors.Corrupt(); }
        }
        budget.Check();
    }
}
