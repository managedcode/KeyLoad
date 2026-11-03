using KeyLoad.Core;

namespace KeyLoad.Query.Search;

/// <summary>Prepares and completes exact ranking within the original authorized search work.</summary>
internal static class ExactSearchExecution
{
    internal static RankedDocument[] Execute(DatabaseEngine database, ITextProjection? textProjection,
        QueryExecutionOptions execution, string principalId, SearchRequest request, ReadExecutionBudget budget)
    {
        var (similarity, eligibility) = Prepare(execution, request, budget);
        var original = database.WithQueryView(principalId, request.Partition, request.Collection,
            request.Vector is null ? Capability.None : Capability.VectorSearch,
            (view, principal, resource) => SearchCapturedExecution.Start(database, textProjection, view,
                principal, resource, request, budget, execution, similarity, eligibility), budget);
        return SearchCapturedExecution.Complete(database, original, request, budget, similarity, eligibility);
    }

    internal static ValueTask<RankedDocument[]> ExecuteVectorAsync(DatabaseEngine database, QueryExecutionOptions execution,
        string principalId, SearchRequest request, ReadExecutionBudget budget)
    {
        var (similarity, eligibility) = Prepare(execution, request, budget);
        return database.WithQueryViewAsync(principalId, request.Partition, request.Collection, Capability.VectorSearch,
            (view, principal, resource) => SearchCapturedExecution.RankVectorAsync(database, view, principal,
                resource, request, budget, similarity!, eligibility), budget);
    }

    private static (PreparedSimilarity? Similarity, FilteredSearchEligibility Eligibility) Prepare(QueryExecutionOptions execution,
        SearchRequest request, ReadExecutionBudget budget)
    {
        budget.Check();
        SearchRequestValidation.Validate(request, false, execution);
        FilteredSearchEligibility.ValidateRequest(request.AllowedIds, budget);
        var similarity = request.Vector is { } vector
            ? PreparedSimilarity.Create(vector.AsMemory(), request.Space!.Metric) : null;
        var eligibility = FilteredSearchEligibility.Create(request.AllowedIds, budget);
        return (similarity, eligibility);
    }

}
