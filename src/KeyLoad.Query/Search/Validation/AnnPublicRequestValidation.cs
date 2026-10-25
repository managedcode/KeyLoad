using KeyLoad.Core;

namespace KeyLoad.Query.Search;

internal static class AnnPublicRequestValidation
{
    internal const int CurrentVersion = 1;
    private const long Empty = 0;
    private const string Invalid = "The approximate search version or vector-only scope is invalid.";

    internal static void Validate(ApproximateSearchRequest request,
        QueryExecutionOptions execution, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Search);
        var search = request.Search;
        if (request.Version != CurrentVersion || request.RequestedMode != AnnPageMode.Approximate || request.Consumer is null
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
