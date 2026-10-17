using System.Collections.Immutable;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed record HybridQualityBranch(
    string Name,
    GlobalBranchKind Kind,
    ImmutableArray<RankedDocument> Hits);
