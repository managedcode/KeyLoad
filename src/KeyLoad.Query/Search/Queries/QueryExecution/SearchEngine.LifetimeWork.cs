using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Core.Search;
using KeyLoad.Query.Search;

namespace KeyLoad.Query;

public sealed partial class SearchEngine
{
    internal Task<RankedDocument[]> SearchBoundAsync(string principalId, SearchRequest request,
        IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        var budget = SearchLifetimeWorkBudget.Create(database, binding, token);
        using var reservation = database.AdmitQuery(token);
        return Task.FromResult(SearchCore(principalId, request, budget));
    }

    internal Task<GraphSearchResult> GraphSearchBoundAsync(string principalId, GraphSearchRequest request,
        IResourceLifetimeWorkBinding binding, CancellationToken token)
        => GraphSearchAsync(principalId, request, SearchLifetimeWorkBudget.Create(database, binding, token), token);

    internal Task<WaitForIndexResult> WaitForIndexBoundAsync(string principalId, WaitForIndexRequest request,
        IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        var budget = SearchLifetimeWorkBudget.Create(database, binding, token);
        using var reservation = database.AdmitQuery(token);
        return Task.FromResult(TextIndexWaitExecution.Execute(database, textProjection, execution,
            readCutObserver, principalId, request, budget));
    }

    internal Task<WaitForAnnIndexResult> WaitForAnnIndexBoundAsync(string principalId, WaitForAnnIndexRequest request,
        IResourceLifetimeWorkBinding binding, CancellationToken token)
        => WaitForAnnIndexAsync(principalId, request, SearchLifetimeWorkBudget.Create(database, binding, token), token);

    internal Task<AnnSearchPage> ApproximateSearchBoundAsync(string principalId, ApproximateSearchRequest request,
        IResourceLifetimeWorkBinding binding, CancellationToken token)
        => ApproximateSearchAsync(principalId, request, SearchLifetimeWorkBudget.Create(database, binding, token), token);

    internal Task<WaitForAnnIndexResult> WaitForAnnIndexBoundAsync(string principalId, WaitForAnnIndexRequest request,
        ReadExecutionBudget budget, AnnReadLifetimeMetadata metadata, IResourceLifetimeWorkBinding binding,
        CancellationToken token)
    {
        budget.RequireResourceLifetimeBinding(binding);
        return WaitForAnnIndexAsync(principalId, request, budget, metadata, token);
    }

    internal Task<AnnSearchPage> ApproximateSearchBoundAsync(string principalId, ApproximateSearchRequest request,
        ReadExecutionBudget budget, AnnReadLifetimeMetadata metadata, IResourceLifetimeWorkBinding binding,
        CancellationToken token)
    {
        budget.RequireResourceLifetimeBinding(binding);
        return ApproximateSearchAsync(principalId, request, budget, metadata, token);
    }

    internal Task<WaitForAnnIndexResult> WaitForAnnIndexBoundAsync(string principalId, WaitForAnnIndexRequest request,
        ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        budget.RequireResourceLifetimeBinding(binding);
        return WaitForAnnIndexAsync(principalId, request, budget, token);
    }

    internal Task<AnnSearchPage> ApproximateSearchBoundAsync(string principalId, ApproximateSearchRequest request,
        ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        budget.RequireResourceLifetimeBinding(binding);
        return ApproximateSearchAsync(principalId, request, budget, token);
    }
}
