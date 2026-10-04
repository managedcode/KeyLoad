using System.Text;

namespace KeyLoad.Query.Features.Search;

internal static class SearchRequestValidation
{
    private const string InvalidSearch = "The search budgets or branch weights are invalid.";
    private const string InvalidText = "A bounded text query and field are required.";
    private const string InvalidVector = "A finite vector and a matching typed vector space are required.";
    private const int MaximumLimit = 1_000;
    private const int MaximumTextBytes = 4_096;
    private const int MaximumVectorDimension = 4_096;

    internal static void Validate(SearchRequest request, DatabaseLimits limits, bool allowNoSearchBranch)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Limit is < 1 or > MaximumLimit || request.Limit > limits.MaxResults || request.FusionConstant < 1
            || !double.IsFinite(request.TextWeight) || !double.IsFinite(request.VectorWeight)
            || request.TextWeight < 0 || request.VectorWeight < 0
            || !allowNoSearchBranch && request.Text is null && request.Vector is null)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidSearch);
        }
        ValidateText(request);
        ValidateVector(request);
    }

    private static void ValidateText(SearchRequest request)
    {
        if (request.Text is { } text && (request.TextField is null || Encoding.UTF8.GetByteCount(text) > MaximumTextBytes))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidText);
        }
    }

    private static void ValidateVector(SearchRequest request)
    {
        if (request.Vector is { } vector && (vector.IsDefault || request.VectorField is null || request.Space is null
            || request.Space.Dimension is < 1 or > MaximumVectorDimension || vector.Length != request.Space.Dimension
            || !Enum.IsDefined(request.Space.Metric)))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidVector);
        }
    }
}
