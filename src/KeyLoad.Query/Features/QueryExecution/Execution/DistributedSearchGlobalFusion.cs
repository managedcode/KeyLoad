using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class DistributedSearchGlobalFusion
{
    private const string IncompleteBranch = "The complete distributed search branch exceeds its original result budget.";
    private const string ConflictingRevision = "The distributed search branches disagree on a selected revision.";
    private const int FirstElement = 0;

    internal static ImmutableArray<GlobalBranchCandidate> Select(SearchRequest request,
        ImmutableArray<GlobalBranchWindow> text, ImmutableArray<GlobalBranchWindow> vector,
        ImmutableArray<string> expectedWindows, GlobalBranchScope scope,
        DatabaseLimits limits, ReadExecutionBudget budget, out SearchRankFusion fusion)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(budget);
        budget.Check();
        var textCandidates = request.Text is null ? ImmutableArray<GlobalBranchCandidate>.Empty
            : Complete(DistributedSearchBranchNames.Text, GlobalBranchKind.Text, text, expectedWindows, scope, limits, budget);
        var vectorCandidates = request.Vector is null ? ImmutableArray<GlobalBranchCandidate>.Empty
            : Complete(DistributedSearchBranchNames.Vector, GlobalBranchKind.Vector, vector, expectedWindows, scope, limits, budget);
        fusion = new SearchRankFusion(request.FusionConstant, request.Limit, budget, request.Explain);
        if (request.Text is not null)
        { fusion.AddBranch(Scores(textCandidates, budget), request.TextWeight, SearchBranchKind.Text); }
        if (request.Vector is not null)
        { fusion.AddBranch(Scores(vectorCandidates, budget), request.VectorWeight, SearchBranchKind.Vector); }
        var ranked = fusion.Select();
        var admission = new GlobalBranchByteAdmission(budget.MaximumResultBytes, budget);
        admission.Accept(PartitionQueryRetention.CandidateArrayBytes(ranked.Length));
        var result = ImmutableArray.CreateBuilder<GlobalBranchCandidate>(ranked.Length);
        foreach (var score in ranked)
        {
            budget.Check();
            var revision = Revision(score.Reference, textCandidates, vectorCandidates, budget);
            var candidate = new GlobalBranchCandidate(score.Reference, revision, score.Score);
            admission.Accept(NativeSerialization.Measure(candidate));
            result.Add(candidate);
        }
        return result.MoveToImmutable();
    }

    private static ImmutableArray<GlobalBranchCandidate> Complete(string name, GlobalBranchKind kind,
        ImmutableArray<GlobalBranchWindow> windows, ImmutableArray<string> expected,
        GlobalBranchScope scope, DatabaseLimits limits, ReadExecutionBudget budget)
    {
        var request = new GlobalBranchMergeRequest(name, kind, scope, expected, limits.MaxResults);
        var merged = GlobalBranchWindowMerger.Merge(request, windows, limits, budget);
        if (!merged.WindowComplete || merged.Approximate || merged.Truncated || !merged.Exhaustive
            || !merged.MissingWindowIds.IsEmpty)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, IncompleteBranch); }
        return merged.Candidates;
    }

    private static SearchScore[] Scores(ImmutableArray<GlobalBranchCandidate> candidates, ReadExecutionBudget budget)
    {
        var admission = new GlobalBranchByteAdmission(budget.MaximumResultBytes, budget);
        admission.Accept(PartitionQueryRetention.CandidateArrayBytes(candidates.Length));
        var result = new SearchScore[candidates.Length];
        for (var index = FirstElement; index < candidates.Length; index++)
        { budget.Check(); result[index] = new(candidates[index].Reference, candidates[index].Score); }
        return result;
    }

    private static long Revision(EntityRef reference, ImmutableArray<GlobalBranchCandidate> text,
        ImmutableArray<GlobalBranchCandidate> vector, ReadExecutionBudget budget)
    {
        long? revision = null;
        foreach (var candidate in text)
        { budget.Check(); if (candidate.Reference == reference) { revision = candidate.Revision; } }
        foreach (var candidate in vector)
        {
            budget.Check();
            if (candidate.Reference != reference)
            { continue; }
            if (revision is not null && revision.Value != candidate.Revision)
            { throw Errors.Fail(ErrorCode.Corruption, ConflictingRevision); }
            revision = candidate.Revision;
        }
        return revision ?? throw Errors.Fail(ErrorCode.Corruption, ConflictingRevision);
    }
}
