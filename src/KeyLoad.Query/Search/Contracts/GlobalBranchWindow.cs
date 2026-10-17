using System.Collections.Immutable;

namespace KeyLoad.Query.Features.Search;

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
