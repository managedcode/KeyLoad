using KeyLoad.Core;

namespace KeyLoad.Query.Search;

internal static class AnnPublicRequestValidation
{
    private const long Empty = 0;
    private const string Invalid = "The approximate search version or vector-only scope is invalid.";

    internal static void Validate(ApproximateSearchRequest request,
        QueryExecutionOptions execution, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(request) /* KLD0041: BCL null validation accepts the request ApproximateSearchRequest reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(request.Search) /* KLD0041: BCL null validation accepts the request.Search SearchRequest reference through its object parameter. */;
        var search = request.Search;
        if (request.RequestedMode != AnnPageMode.Approximate || request.Consumer is null
            || request.IndexGeneration <= Empty || request.Consumer.Partition != search.Partition
            || search.Text is not null || search.TextField is not null || search.Explain
            || search.Vector is null || search.Space is null || search.VectorField is null)
        {
            throw Errors.Fail(ErrorCode.Validation, Invalid);
        }
        JsonData.Identifier(request.Consumer.Name);
        SearchRequestValidation.Validate(search, false, execution);
        FilteredSearchEligibility.ValidateRequest(search.AllowedIds, budget);
    }
}
