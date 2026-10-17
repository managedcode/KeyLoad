using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal static class HybridQualityWindowHarness
{
    internal static readonly ImmutableArray<int> Widths = [1, 4, 8, 32];

    internal static ImmutableArray<HybridQualityWindowObservation> Evaluate(
        ImmutableArray<HybridQualityBranch> branches, HybridQualityQuery query,
        ImmutableArray<HybridQualityJudgment> judgments, DatabaseLimits limits)
    {
        var observations = ImmutableArray.CreateBuilder<HybridQualityWindowObservation>(Widths.Length);
        HashSet<string>? previous = null;
        foreach (var width in Widths)
        {
            var run = EvaluateWidth(branches, query, judgments, limits, width);
            var current = run.CandidateIds.ToHashSet(StringComparer.Ordinal);
            if (previous is not null && !previous.IsSubsetOf(current))
            {
                throw new InvalidOperationException("Increasing a branch window removed an observed candidate.");
            }
            previous = current;
            observations.Add(run);
        }
        return observations.MoveToImmutable();
    }

    private static HybridQualityWindowObservation EvaluateWidth(ImmutableArray<HybridQualityBranch> branches,
        HybridQualityQuery query, ImmutableArray<HybridQualityJudgment> judgments, DatabaseLimits limits, int width)
    {
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(limits));
        var fusion = new SearchRankFusion(HybridQualityCorpus.FusionConstant,
            HybridQualityCorpus.ResultLimit, budget);
        var union = new HashSet<string>(StringComparer.Ordinal);
        var truncated = false;
        foreach (var branch in branches)
        {
            var source = branch.Hits.Take(width).ToArray();
            var window = CreateWindow(branch, source, branch.Hits.Length > width);
            var request = new GlobalBranchMergeRequest(branch.Name, branch.Kind, Scope,
                [HybridQualityCorpus.WindowId], HybridQualityCorpus.ResultLimit, AllowIncomplete: true);
            var merged = GlobalBranchWindowMerger.Merge(request, [window], limits, budget);
            truncated |= merged.Truncated;
            foreach (var candidate in merged.Candidates)
            {
                union.Add(candidate.Reference.Id);
            }
            var scores = merged.Candidates.Select(candidate =>
                new SearchScore(candidate.Reference, candidate.Score)).ToArray();
            fusion.AddBranch(scores, HybridQualityCorpus.BranchWeight);
        }
        var fused = fusion.Select();
        var candidateIds = union.Order(StringComparer.Ordinal).ToImmutableArray();
        var fusedIds = fused.Select(candidate => candidate.Reference.Id).ToImmutableArray();
        var metrics = HybridQualityMetricOracle.Compute(fusedIds, judgments, query.EligibleIds);
        var candidateRecall = HybridQualityMetricOracle.CandidateRecall(candidateIds, judgments, query.EligibleIds);
        return new(width, candidateIds, candidateRecall, fusedIds,
            [.. fused.Select(candidate => candidate.Score)], metrics, truncated);
    }

    private static GlobalBranchWindow CreateWindow(HybridQualityBranch branch,
        IReadOnlyList<RankedDocument> hits, bool truncated)
    {
        var candidates = hits.Select(hit => new GlobalBranchCandidate(hit.Document.Reference,
            hit.Document.Revision, hit.Score)).ToImmutableArray();
        return new(branch.Name, branch.Kind, HybridQualityCorpus.WindowId, Scope, candidates,
            Complete: true, Approximate: false, Truncated: truncated, Exhaustive: !truncated);
    }

    private static GlobalBranchScope Scope => new(HybridQualityCorpus.ScoreProfile,
        HybridQualityCorpus.CorpusScope, HybridQualityCorpus.StatisticsEpoch);
}
