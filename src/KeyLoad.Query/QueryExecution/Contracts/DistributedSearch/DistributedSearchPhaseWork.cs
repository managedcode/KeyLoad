using System.Collections.Immutable;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Query.Features.QueryExecution;

internal sealed record DistributedSearchPhaseWork(
    DistributedSearchPhase Phase,
    SearchRequest Search,
    DistributedTextWitness? Witness,
    DistributedTextStatistics? Statistics,
    GlobalBranchScope? Scope,
    string? SourceWindowId,
    ImmutableArray<GlobalBranchCandidate> Selected,
    int MaxResultBytes);
