using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.Search;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Query;

public sealed partial class SearchEngine
{
    internal Task<RankedDocument[]> SearchBoundAsync(string principalId, SearchRequest request,
        CancellationToken token, IResourceLifetimeWorkBinding binding)
        => SearchLifetimeWorkBudget.RunNativeAsync(database, execution, binding, token,
            budget => SearchCore(principalId, request, budget));

    internal Task<GraphSearchResult> GraphSearchBoundAsync(string principalId, GraphSearchRequest request,
        CancellationToken token, IResourceLifetimeWorkBinding binding)
        => GraphSearchAsync(principalId, request, SearchLifetimeWorkBudget.Create(database, execution, binding, token), token);

    internal Task<WaitForIndexResult> WaitForIndexBoundAsync(string principalId, WaitForIndexRequest request,
        CancellationToken token, IResourceLifetimeWorkBinding binding)
        => SearchLifetimeWorkBudget.RunNativeAsync(database, execution, binding, token,
            budget => TextIndexWaitExecution.Execute(database, textProjection, execution,
                readCutObserver, principalId, request, budget));

    internal Task<WaitForAnnIndexResult> WaitForAnnIndexBoundAsync(string principalId, WaitForAnnIndexRequest request,
        CancellationToken token, IResourceLifetimeWorkBinding binding)
        => WaitForAnnIndexAsync(principalId, request, SearchLifetimeWorkBudget.Create(database, execution, binding, token), token);

    internal Task<AnnSearchPage> ApproximateSearchBoundAsync(string principalId, ApproximateSearchRequest request,
        CancellationToken token, IResourceLifetimeWorkBinding binding)
        => ApproximateSearchAsync(principalId, request, SearchLifetimeWorkBudget.Create(database, execution, binding, token), token);

    internal Task<WaitForAnnIndexResult> WaitForAnnIndexBoundAsync(string principalId, WaitForAnnIndexRequest request,
        ReadExecutionBudget budget, AnnReadLifetimeMetadata metadata, CancellationToken token,
        IResourceLifetimeWorkBinding binding)
    {
        budget.RequireResourceLifetimeBinding(binding);
        return WaitForAnnIndexAsync(principalId, request, budget, metadata, token);
    }

    internal Task<AnnSearchPage> ApproximateSearchBoundAsync(string principalId, ApproximateSearchRequest request,
        ReadExecutionBudget budget, AnnReadLifetimeMetadata metadata, CancellationToken token,
        IResourceLifetimeWorkBinding binding)
    {
        budget.RequireResourceLifetimeBinding(binding);
        return ApproximateSearchAsync(principalId, request, budget, metadata, token);
    }

    internal Task<WaitForAnnIndexResult> WaitForAnnIndexBoundAsync(string principalId, WaitForAnnIndexRequest request,
        ReadExecutionBudget budget, CancellationToken token, IResourceLifetimeWorkBinding binding)
    {
        budget.RequireResourceLifetimeBinding(binding);
        return WaitForAnnIndexAsync(principalId, request, budget, token);
    }

    internal Task<AnnSearchPage> ApproximateSearchBoundAsync(string principalId, ApproximateSearchRequest request,
        ReadExecutionBudget budget, CancellationToken token, IResourceLifetimeWorkBinding binding)
    {
        budget.RequireResourceLifetimeBinding(binding);
        return ApproximateSearchAsync(principalId, request, budget, token);
    }
}
