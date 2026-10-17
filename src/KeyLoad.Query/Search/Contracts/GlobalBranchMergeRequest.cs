using System.Collections.Immutable;

namespace KeyLoad.Query.Features.Search;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(GlobalBranchProtocol.MergeRequestAlias)]
internal sealed record GlobalBranchMergeRequest(
    [property: global::Orleans.Id(0)] string BranchName,
    [property: global::Orleans.Id(1)] GlobalBranchKind Kind,
    [property: global::Orleans.Id(2)] GlobalBranchScope Scope,
    [property: global::Orleans.Id(3)] ImmutableArray<string> ExpectedWindowIds,
    [property: global::Orleans.Id(4)] int Limit,
    [property: global::Orleans.Id(5)] bool AllowIncomplete = false);
