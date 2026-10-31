using System.Collections.Immutable;
using KeyLoad.Query.Search;

namespace KeyLoad.Query.QueryExecution;

internal sealed record DistributedSearchPhaseWork(
    DistributedSearchPhase Phase,
    SearchRequest Search,
    DistributedTextWitness? Witness,
    DistributedTextStatistics? Statistics,
    GlobalBranchScope? Scope,
    string? SourceWindowId,
    ImmutableArray<GlobalBranchCandidate> Selected);
