using System.Collections.Immutable;

namespace KeyLoad.Query.Features.Search;

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
