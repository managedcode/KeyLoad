using System.Collections.Immutable;
using KeyLoad.Core.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query.Search;

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
