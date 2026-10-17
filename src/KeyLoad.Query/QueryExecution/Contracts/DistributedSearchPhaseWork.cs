using System.Collections.Immutable;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Query.Features.QueryExecution;

internal sealed record DistributedSearchPhaseWork(
    DistributedSearchPhase Phase,
    SearchRequest Search,
    DistributedTextWitnessV1? Witness,
    DistributedTextStatisticsV1? Statistics,
    GlobalBranchScope? Scope,
    string? SourceWindowId,
    ImmutableArray<GlobalBranchCandidate> Selected,
    int MaxResultBytes);
