using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Query.Features.QueryExecution;

namespace KeyLoad.Query;

internal static class QueryValidation
{
    private const string QueryRequestPartitionAndQueryAreRequiredDetail = "A query request, partition and query are required.";
    private const int VersionOne = 1;
    private const string QueryASTVersionIsUnsupportedDetail = "The query AST version is unsupported.";
    private const int MinimumPositiveCount = 1;
    private const string QueryStructureExceedsItsBudgetDetail = "The query structure exceeds its budget.";
    private const int RootPredicateDepth = 1;
    private const string QueryRequestExceedsItsByteBudgetDetail = "The query request exceeds its byte budget.";
    private const string ModelQuerySourceIsUnsupportedDetail = "The model query source is unsupported.";
    private const string ModelQueryRequiresExplicitFullScanConsentDetail = "A model query requires explicit full-scan consent.";
    private const string ModelQueryContinuationIsNotSupportedDetail = "Model query continuation is not supported.";
    private const int AdjacentElementOffset = 1;
    private const string ModelQuerySourceArgumentsAreInvalidDetail = "The model query source arguments are invalid.";
    private const string QueryProjectionEntryIsMissingDetail = "A query projection entry is missing.";
    private const string QueryProjectionHasDuplicateAliasesOrAnInvalidStarDetail = "The query projection has duplicate aliases or an invalid star.";
    private const string QueryOrderingEntryIsMissingDetail = "A query ordering entry is missing.";
    private const int EmptyElementCount = 0;

    private const string InvalidPath = "The query field path is invalid.";

    public static AstQueryRequest Normalize(AstQueryRequest request, DatabaseLimits limits, QueryExecutionOptions execution)
    {
        if (request is null || request.Partition is null || request.Query is null)
        {
            throw Errors.Fail(ErrorCode.Validation, QueryRequestPartitionAndQueryAreRequiredDetail);
        }
        if (request.AstVersion != VersionOne)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, QueryASTVersionIsUnsupportedDetail);
        }

        var query = request.Query;
        DatabaseEngine.ValidatePartition(request.Partition);
        JsonData.Identifier(query.Collection);
        ValidateModelSource(request, query);
        if (query.Projection.IsDefault || query.Order.IsDefault
            || query.Projection.Length < MinimumPositiveCount || query.Projection.Length > execution.MaximumProjection
            || query.Order.Length > execution.MaximumOrdering || query.Limit < MinimumPositiveCount || query.Limit > limits.MaxResults
            || request.Parameters?.Count > execution.MaximumParameters)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, QueryStructureExceedsItsBudgetDetail);
        }

        var projection = NormalizeProjection(query.Projection, limits);
        var order = NormalizeOrder(query.Order, limits);
        var predicates = new QueryPredicateNormalizer(limits.MaxQueryDepth, limits.MaxQueryTokens, execution.MaximumInValues,
            value => JsonData.Validate(value, limits, false));
        var filter = query.Filter is null ? null : predicates.Filter(query.Filter, RootPredicateDepth);
        var parameters = NormalizeParameters(request.Parameters, predicates);
        var normalized = request with
        {
            Query = query with { Alias = null, Projection = projection, Order = order, Filter = filter },
            Parameters = parameters
        };
        if (JsonDefaults.Serialize(normalized).Length > limits.MaxQueryBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, QueryRequestExceedsItsByteBudgetDetail);
        }
        return normalized;
    }

    private static void ValidateModelSource(AstQueryRequest request, SelectQuery query)
    {
        if (query.ModelSource is not { } source)
        {
            return;
        }
        if (!Enum.IsDefined(source.Kind))
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, ModelQuerySourceIsUnsupportedDetail);
        }
        JsonData.Identifier(source.Item);
        if (!request.AllowFullScan)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, ModelQueryRequiresExplicitFullScanConsentDetail);
        }
        if (request.Cursor is not null)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, ModelQueryContinuationIsNotSupportedDetail);
        }
        if (source.Kind == ModelQuerySourceKind.Events && source.Generation < MinimumPositiveCount
            || source.Kind == ModelQuerySourceKind.QueueMessages
            && (source.Generation != AdjacentElementOffset || source.Item != query.Collection))
        {
            throw Errors.Fail(ErrorCode.Validation, ModelQuerySourceArgumentsAreInvalidDetail);
        }
    }

    private static ImmutableArray<Selection> NormalizeProjection(ImmutableArray<Selection> input, DatabaseLimits limits)
    {
        var projection = input.Select(selection =>
        {
            if (selection is null)
            {
                throw Errors.Fail(ErrorCode.Validation, QueryProjectionEntryIsMissingDetail);
            }
            Path(selection.Path, limits.MaxQueryDepth, true);
            JsonData.Identifier(selection.Alias);
            return selection with { };
        }).ToImmutableArray();
        if (projection.Select(s => s.Alias).Distinct(StringComparer.Ordinal).Count() != projection.Length
            || projection.Length > MinimumPositiveCount && projection.Any(s => s.Path == SqlSyntax.Star))
        {
            throw Errors.Fail(ErrorCode.Validation, QueryProjectionHasDuplicateAliasesOrAnInvalidStarDetail);
        }
        return projection;
    }

    private static ImmutableArray<Ordering> NormalizeOrder(ImmutableArray<Ordering> input, DatabaseLimits limits)
        => input.Select(item =>
        {
            if (item is null)
            {
                throw Errors.Fail(ErrorCode.Validation, QueryOrderingEntryIsMissingDetail);
            }
            Path(item.Path, limits.MaxQueryDepth);
            return item with { };
        }).ToImmutableArray();

    private static Dictionary<string, JsonElement>? NormalizeParameters(Dictionary<string, JsonElement>? parameters,
        QueryPredicateNormalizer predicates) => parameters is not { Count: > EmptyElementCount } ? null : parameters.ToDictionary(item =>
    {
        JsonData.Identifier(item.Key);
        return item.Key;
    }, item => predicates.Scalar(item.Value), StringComparer.Ordinal);

    internal static void Path(string path, int maximumDepth, bool star = false)
    {
        if (star && path == SqlSyntax.Star)
        {
            return;
        }
        if (string.IsNullOrEmpty(path) || JsonData.PathSegments(path).Length > maximumDepth)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidPath);
        }
    }
}
