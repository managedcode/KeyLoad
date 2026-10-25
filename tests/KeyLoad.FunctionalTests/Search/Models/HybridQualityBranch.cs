using System.Collections.Immutable;
using KeyLoad.Query.Search;

namespace KeyLoad.FunctionalTests.Search;

internal sealed record HybridQualityBranch(
    string Name,
    GlobalBranchKind Kind,
    ImmutableArray<RankedDocument> Hits);
