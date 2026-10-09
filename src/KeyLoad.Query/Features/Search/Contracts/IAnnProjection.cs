using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query.Features.Search;

internal interface IAnnProjection
{
    IAnnProjectionLease Acquire(IKeyValueView view, AnnProjectionSelection request,
        ReadExecutionBudget budget);
}

internal interface IAnnProjectionLease : IDisposable
{
    long IndexedAppliedPosition { get; }
    long IndexGeneration { get; }
    ImmutableArray<VectorRecord> Records { get; }
    AnnSeedWork ScopeWork { get; }
    IOptions<AnnSeedOptions> CallerSeedOptions { get; }
    AnnSearchResult Search(ReadOnlyMemory<float> query, int limit,
        ReadOnlyMemory<ulong> eligible, AnnWorkBudget budget);
}
