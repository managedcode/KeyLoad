using System.Text;

namespace KeyLoad.Query.Features.Search;

internal static class SearchRequestValidation
{
    private const int MinimumPositiveCount = 1;
    private const int EmptyElementCount = 0;

    private const string InvalidSearch = "The search budgets or branch weights are invalid.";
    private const string InvalidText = "A bounded text query and field are required.";
    private const string InvalidVector = "A finite vector and a matching typed vector space are required.";
    private const int MaximumVectorDimension = 4_096;

    internal static void Validate(SearchRequest request, DatabaseLimits limits, bool allowNoSearchBranch, QueryExecutionOptions execution)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Limit < MinimumPositiveCount || request.Limit > execution.MaximumSearchResults || request.Limit > limits.MaxResults || request.FusionConstant < MinimumPositiveCount
            || !double.IsFinite(request.TextWeight) || !double.IsFinite(request.VectorWeight)
            || request.TextWeight < EmptyElementCount || request.VectorWeight < EmptyElementCount
            || !allowNoSearchBranch && request.Text is null && request.Vector is null)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidSearch);
        }
        ValidateText(request, execution.MaximumSearchTextBytes);
        ValidateVector(request);
    }

    private static void ValidateText(SearchRequest request, int maximumTextBytes)
    {
        if (request.Text is { } text && (request.TextField is null || Encoding.UTF8.GetByteCount(text) > maximumTextBytes))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidText);
        }
    }

    private static void ValidateVector(SearchRequest request)
    {
        if (request.Vector is { } vector && (vector.IsDefault || request.VectorField is null || request.Space is null
            || request.Space.Dimension is < MinimumPositiveCount or > MaximumVectorDimension || vector.Length != request.Space.Dimension
            || !Enum.IsDefined(request.Space.Metric)))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidVector);
        }
    }
}
