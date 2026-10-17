using System.Collections.Immutable;

namespace KeyLoad.Query.Features.Search;

internal static class GlobalBranchProtocol
{
    internal const string KindAlias = "keyload.query.global-branch-kind.v1";
    internal const string ScopeAlias = "keyload.query.global-branch-scope.v1";
    internal const string CandidateAlias = "keyload.query.global-branch-candidate.v1";
    internal const string WindowAlias = "keyload.query.global-branch-window.v1";
    internal const string MergeRequestAlias = "keyload.query.global-branch-merge-request.v1";
    internal const string MergeResultAlias = "keyload.query.global-branch-merge-result.v1";
}

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(GlobalBranchProtocol.KindAlias)]
internal enum GlobalBranchKind
{
    Text = 0,
    Vector = 1,
    Graph = 2
}

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(GlobalBranchProtocol.ScopeAlias)]
internal sealed record GlobalBranchScope(
    [property: global::Orleans.Id(0)] string ScoreProfile,
    [property: global::Orleans.Id(1)] string CorpusScope,
    [property: global::Orleans.Id(2)] string StatisticsEpoch);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(GlobalBranchProtocol.CandidateAlias)]
internal sealed record GlobalBranchCandidate(
    [property: global::Orleans.Id(0)] EntityRef Reference,
    [property: global::Orleans.Id(1)] long Revision,
    [property: global::Orleans.Id(2)] double Score);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(GlobalBranchProtocol.WindowAlias)]
internal sealed record GlobalBranchWindow(
    [property: global::Orleans.Id(0)] string BranchName,
    [property: global::Orleans.Id(1)] GlobalBranchKind Kind,
    [property: global::Orleans.Id(2)] string SourceWindowId,
    [property: global::Orleans.Id(3)] GlobalBranchScope Scope,
    [property: global::Orleans.Id(4)] ImmutableArray<GlobalBranchCandidate> Candidates,
    [property: global::Orleans.Id(5)] bool Complete,
    [property: global::Orleans.Id(6)] bool Approximate,
    [property: global::Orleans.Id(7)] bool Truncated,
    [property: global::Orleans.Id(8)] bool Exhaustive = false);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(GlobalBranchProtocol.MergeRequestAlias)]
internal sealed record GlobalBranchMergeRequest(
    [property: global::Orleans.Id(0)] string BranchName,
    [property: global::Orleans.Id(1)] GlobalBranchKind Kind,
    [property: global::Orleans.Id(2)] GlobalBranchScope Scope,
    [property: global::Orleans.Id(3)] ImmutableArray<string> ExpectedWindowIds,
    [property: global::Orleans.Id(4)] int Limit,
    [property: global::Orleans.Id(5)] bool AllowIncomplete = false);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(GlobalBranchProtocol.MergeResultAlias)]
internal sealed record GlobalBranchMergeResult(
    [property: global::Orleans.Id(0)] ImmutableArray<GlobalBranchCandidate> Candidates,
    [property: global::Orleans.Id(1)] ImmutableArray<string> MissingWindowIds,
    [property: global::Orleans.Id(2)] bool WindowComplete,
    [property: global::Orleans.Id(3)] bool Approximate,
    [property: global::Orleans.Id(4)] bool Truncated,
    [property: global::Orleans.Id(5)] bool Exhaustive,
    [property: global::Orleans.Id(6)] int ExaminedCandidateCount);
