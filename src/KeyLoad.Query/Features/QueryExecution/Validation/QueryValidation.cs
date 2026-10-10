using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Query.Features.QueryExecution;

namespace KeyLoad.Query;

internal static class QueryValidation
{
    private const string QueryRequestPartitionAndQueryAreRequiredDetail = "A query request, partition and query are required.";
    private const int VersionOne = 1;
    private const int VersionTwo = 2;
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
        if (request.AstVersion is not (VersionOne or VersionTwo))
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
        ValidateJoinVersion(request, query, projection);
        var join = NormalizeJoin(query.InnerJoin, limits);
        if (join is not null)
        {
            order = NormalizeJoinOrder(order, limits);
        }

        var predicates = new QueryPredicateNormalizer(limits.MaxQueryDepth, limits.MaxQueryTokens, execution.MaximumInValues,
            value => JsonData.Validate(value, limits, false));
        var filter = query.Filter is null ? null : predicates.Filter(query.Filter, RootPredicateDepth);
        var parameters = NormalizeParameters(request.Parameters, predicates);
        var normalized = request with
        {
            Query = query with { Alias = join is null ? null : query.Alias, Projection = projection, Order = order, Filter = filter, InnerJoin = join },
            Parameters = parameters
        };
        if (JsonDefaults.Serialize(normalized).Length > limits.MaxQueryBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, QueryRequestExceedsItsByteBudgetDetail);
        }
        return normalized;
    }

    private static void ValidateJoinVersion(AstQueryRequest request, SelectQuery query, ImmutableArray<Selection> projection)
    {
        var hasJoin = query.InnerJoin is not null;
        if (request.AstVersion == VersionOne && hasJoin
            || hasJoin && (query.Alias is null || query.ModelSource is not null || request.Cursor is not null || query.Filter is not null || query.Explain
                || !request.AllowFullScan || projection.Any(selection => selection.SourceAlias is null)
                || StringComparer.Ordinal.Equals(query.Collection, query.InnerJoin!.Collection)
                || StringComparer.Ordinal.Equals(query.Alias, query.InnerJoin.Alias))
            || !hasJoin && projection.Any(selection => selection.SourceAlias is not null))
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, QueryASTVersionIsUnsupportedDetail);
        }
    }

    private static InnerJoinClause? NormalizeJoin(InnerJoinClause? join, DatabaseLimits limits)
    {
        if (join is null)
        {
            return null;
        }
        JsonData.Identifier(join.Collection);
        JsonData.Identifier(join.Alias);
        var leftPath = CanonicalPath(join.LeftKeyPath, limits.MaxQueryDepth);
        var rightPath = CanonicalPath(join.RightKeyPath, limits.MaxQueryDepth);
        return join with { LeftKeyPath = leftPath, RightKeyPath = rightPath };
    }

    private static string CanonicalPath(string path, int maximumDepth)
    {
        Path(path, maximumDepth);
        return JsonData.Path(JsonData.PathSegments(path));
    }

    private static ImmutableArray<Ordering> NormalizeJoinOrder(ImmutableArray<Ordering> order, DatabaseLimits limits)
        => order.Select(item => item with { Path = CanonicalPath(item.Path, limits.MaxQueryDepth) }).ToImmutableArray();

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
            && (source.Generation != AdjacentElementOffset || source.Item != query.Collection)
            || source.Kind == ModelQuerySourceKind.TopicEvents
            && (source.Generation < MinimumPositiveCount || source.Item != query.Collection))
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
            if (selection.SourceAlias is not null)
            {
                JsonData.Identifier(selection.SourceAlias);
            }
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
