using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Query.QueryExecution;

namespace KeyLoad.Query;

internal static class QueryValidation
{
    private const string QueryRequestPartitionAndQueryAreRequiredDetail = "A query request, partition and query are required.";
    private const string QueryASTVersionIsUnsupportedDetail = "The query AST version is unsupported.";
    private const int MinimumPositiveCount = 1;
    private const string QueryStructureExceedsItsBudgetDetail = "The query structure exceeds its budget.";
    private const int RootPredicateDepth = 1;
    private const string QueryWindowIsNotRepresentableDetail = "The query offset and limit do not form a valid row window.";
    private const string ModelQuerySourceIsUnsupportedDetail = "The model query source is unsupported.";
    private const string ModelQueryRequiresExplicitFullScanConsentDetail = "A model query requires explicit full-scan consent.";
    private const string ModelQueryContinuationIsNotSupportedDetail = "Model query continuation is not supported.";
    private const int AdjacentElementOffset = 1;
    private const string ModelQuerySourceArgumentsAreInvalidDetail = "The model query source arguments are invalid.";
    private const int EmptyElementCount = 0;

    private const string InvalidPath = "The query field path is invalid.";
    private const string InvalidTypedJoinSource = "The bounded relational join is invalid.";
    private const string InvalidQueueJoinSource = "The queue entity-reference join is unsupported.";
    private const string InvalidEventJoinSource = "The event entity-reference join is unsupported.";

    public static AstQueryRequest Normalize(AstQueryRequest request, QueryExecutionOptions execution)
    {
        if (request is null || request.Partition is null || request.Query is null)
        {
            throw Errors.Fail(ErrorCode.Validation, QueryRequestPartitionAndQueryAreRequiredDetail);
        }
        if (request.AstVersion != SqlOperationProtocol.DefaultQueryDialectVersion)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, QueryASTVersionIsUnsupportedDetail);
        }

        var query = request.Query;
        EngineResourceAccess.ValidatePartition(request.Partition);
        JsonData.Identifier(query.Collection);
        ValidateModelSource(request, query);
        if (query.Projection.IsDefault || query.Order.IsDefault
            || query.Projection.Length < MinimumPositiveCount || query.Projection.Length > execution.MaximumProjection
            || query.Order.Length > execution.MaximumOrdering
            || request.Parameters?.Count > execution.MaximumParameters)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, QueryStructureExceedsItsBudgetDetail);
        }
        // A window needs a positive limit and a nonnegative offset that ends within a 32-bit row position.
        if (query.Limit < MinimumPositiveCount || query.Offset < EmptyElementCount
            || (long)query.Offset + query.Limit > int.MaxValue)
        {
            throw Errors.Fail(ErrorCode.Validation, QueryWindowIsNotRepresentableDetail);
        }

        var projection = QuerySelectionNormalization.Projection(query.Projection);
        var order = QuerySelectionNormalization.Order(query.Order);
        ValidateJoinShape(request, query, projection);
        var join = (NormalizeJoin(query.InnerJoin)).Value;
        if (join is not null)
        {
            JsonData.Identifier(query.Alias!);
            order = NormalizeJoinOrder(order);
        }

        var predicates = new QueryPredicateNormalizer(execution.MaximumInValues,
            value => JsonData.Validate(value, false));
        var filter = query.Filter is null ? null : predicates.Filter(query.Filter, RootPredicateDepth);
        var parameters = (NormalizeParameters(request.Parameters, predicates)).Value;
        if (query.ModelSource is null)
        {
            global::KeyLoad.Query.QueryExecution.EventEntityRefJoinBinding.ValidateJoinPredicate(query, filter, parameters);
        }
        else if (join is not null)
        {
            PredicateEvaluator.CheckParameters(filter, parameters);
        }
        var normalized = request with
        {
            Query = query with
            {
                Alias = join is null ? null : query.Alias,
                Projection = projection,
                Order = order,
                Filter = filter,
                InnerJoin = join
            },
            Parameters = parameters
        };
        return normalized;
    }

    private static void ValidateJoinShape(AstQueryRequest request, SelectQuery query, ImmutableArray<Selection> projection)
    {
        var hasJoin = query.InnerJoin is not null;
        if (hasJoin && (query.Alias is null || query.ModelSource is not null
                && !global::KeyLoad.Query.QueryExecution.QueueEntityRefJoinBinding.IsSupportedSource(query)
                && !global::KeyLoad.Query.QueryExecution.EventEntityRefJoinBinding.IsSupportedSource(query)
                || request.Cursor is not null || query.Explain
                || !request.AllowFullScan || projection.Any(selection => selection.SourceAlias is null)
                || StringComparer.Ordinal.Equals(query.Collection, query.InnerJoin!.Collection)
                || StringComparer.Ordinal.Equals(query.Alias, query.InnerJoin.Alias))
            || !hasJoin && projection.Any(selection => selection.SourceAlias is not null))
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, QueryASTVersionIsUnsupportedDetail);
        }
        ValidateJoinSources(query, projection);
    }

    private static void ValidateJoinSources(SelectQuery query, ImmutableArray<Selection> projection)
    {
        if (query.InnerJoin is not { } join
            || projection.All(selection => selection.SourceAlias == query.Alias || selection.SourceAlias == join.Alias))
        {
            return;
        }
        if (query.ModelSource is null)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidTypedJoinSource);
        }
        throw Errors.Fail(ErrorCode.UnsupportedCapability,
            query.ModelSource.Kind == ModelQuerySourceKind.Events ? InvalidEventJoinSource : InvalidQueueJoinSource);
    }

    private static global::ManagedCode.Communication.Result<InnerJoinClause?> NormalizeJoin(InnerJoinClause? join)
    {
        if (join is null)
        {
            return global::ManagedCode.Communication.Result<InnerJoinClause?>.FailNotFound(nameof(NormalizeJoin));
        }
        JsonData.Identifier(join.Collection);
        JsonData.Identifier(join.Alias);
        var leftPath = CanonicalPath(join.LeftKeyPath);
        var rightPath = CanonicalPath(join.RightKeyPath);
        return global::ManagedCode.Communication.Result<InnerJoinClause?>.Succeed(((InnerJoinClause?)(join with
        {
            LeftKeyPath = leftPath,
            RightKeyPath = rightPath
        })));
    }

    private static string CanonicalPath(string path)
    {
        Path(path);
        return JsonData.Path(JsonData.PathSegments(path));
    }

    private static ImmutableArray<Ordering> NormalizeJoinOrder(ImmutableArray<Ordering> order)
        => order.Select(item => item with { Path = CanonicalPath(item.Path) }).ToImmutableArray();

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
        if (request.Cursor is not null || query.Offset != EmptyElementCount)
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

    private static global::ManagedCode.Communication.Result<Dictionary<string, JsonElement>?> NormalizeParameters(Dictionary<string, JsonElement>? parameters,
        QueryPredicateNormalizer predicates) => parameters is not { Count: > EmptyElementCount } ? global::ManagedCode.Communication.Result<Dictionary<string, JsonElement>?>.FailNotFound(nameof(NormalizeParameters)) : global::ManagedCode.Communication.Result<Dictionary<string, JsonElement>?>.Succeed(((Dictionary<string, JsonElement>?)(parameters.ToDictionary(item =>
    {
        JsonData.Identifier(item.Key);
        return item.Key;
    }, item => predicates.Scalar(item.Value), StringComparer.Ordinal))));

    internal static void Path(string path, bool star = false)
    {
        if (star && path == SqlSyntax.Star)
        {
            return;
        }
        if (string.IsNullOrEmpty(path))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidPath);
        }
    }
}
