using System.Collections.Immutable;
using KeyLoad.Core.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query.Features.Search;

internal interface IAnnProjectionLease : IDisposable
{
    long IndexedAppliedPosition
    {
        get;
    }
    long IndexGeneration
    {
        get;
    }
    ImmutableArray<VectorRecord> Records
    {
        get;
    }
    AnnSeedWork ScopeWork
    {
        get;
    }
    IOptions<AnnSeedOptions> CallerSeedOptions
    {
        get;
    }
    AnnSearchResult Search(ReadOnlyMemory<float> query, int limit,
        ReadOnlyMemory<ulong> eligible, AnnWorkBudget budget);
}
