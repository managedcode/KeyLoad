using KeyLoad.Core;
using KeyLoad.Core.Features.RelationalStorage;
using KeyLoad.Storage;

namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>Binds the closed event-reference join to current persisted resources and field authority.</summary>
internal static class EventEntityRefJoinBinding
{
    internal const string ReferencePath = "/payload/to";
    internal const string EntityReferenceOperand = "/entity_ref";
    internal const string AccessPath = "bounded-event-entity-reference-inner-join";
    private const int SingleOrder = 1;
    private const int First = 0;
    private const int SinglePath = 1;
    private const string Invalid = "The event entity-reference join is unsupported.";
    private const string IdentifierField = "eventId";
    private const string EventTypeField = "eventType";
    private const string SchemaVersionField = "schemaVersion";
    private const string RevisionField = "revision";
    private const string SequenceField = "eventSequence";
    private const string RecordedAtField = "recordedAt";
    private const string PayloadField = "payload";
    private const string HeadersField = "headers";
    private const string LeftIdentityPath = "/revision";
    private static readonly string[] EventFields =
        [IdentifierField, EventTypeField, SchemaVersionField, RevisionField, SequenceField, RecordedAtField, PayloadField, HeadersField];

    internal static bool IsSupportedSource(SelectQuery query)
        => query.ModelSource?.Kind == ModelQuerySourceKind.Events
           && query.InnerJoin is { LeftKeyPath: ReferencePath, RightKeyPath: EntityReferenceOperand };

    internal static void ValidateJoinPredicate(SelectQuery query, Predicate? filter,
        Dictionary<string, System.Text.Json.JsonElement>? parameters)
    {
        if (query.ModelSource?.Kind != ModelQuerySourceKind.Events)
        {
            QueueEntityRefJoinBinding.ValidateJoinPredicate(query, filter, parameters);
            return;
        }
        if (query.InnerJoin is null)
        {
            return;
        }
        if (!IsSupportedSource(query))
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, Invalid);
        }
        ValidatePredicatePaths(filter, parameters);
    }

    internal static void ValidatePredicatePaths(Predicate? filter, Dictionary<string, System.Text.Json.JsonElement>? parameters)
    {
        foreach (var path in PredicateEvaluator.Fields(filter))
        {
            if (path is not SqlSyntax.MetadataIdPath and not SqlSyntax.MetadataRevisionPath && !IsEventPath(path))
            {
                throw Errors.Fail(ErrorCode.UnsupportedCapability, Invalid);
            }
        }
        PredicateEvaluator.CheckParameters(filter, parameters);
    }

    internal static ResourceDefinition Bind(DatabaseEngine database, IKeyValueView view,
        PrincipalRecord principal, ResourceDefinition streamSet, AstQueryRequest request, ReadExecutionBudget budget)
    {
        var model = ModelQueryPolicies.Rebase(streamSet);
        database.Authorization.RequireSchemaVisibility(principal, model);
        var query = request.Query;
        var join = query.InnerJoin;
        if (!IsSupportedSource(query) || query.Alias is null || join is null || request.Cursor is not null
            || query.Explain || !request.AllowFullScan || query.Order.Length != SingleOrder
            || query.Order[First].Path != LeftIdentityPath || query.Order[First].Descending
            || query.Projection.IsDefaultOrEmpty || query.Projection.Any(item => item.Path == SqlSyntax.Star))
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, Invalid);
        }
        database.Authorization.Require(principal, request.Partition, join.Collection, Capability.Query | Capability.DocumentsRead);
        var target = ResourceLifetimeReadBinding.BindAuthorizedReadResource(budget.CreateView(view), request.Partition, join.Collection, ResourceKind.Collection);
        database.Authorization.RequireSchemaVisibility(principal, target);
        var schema = target.RelationalSchema ?? throw Errors.Fail(ErrorCode.UnsupportedCapability, Invalid);
        RelationalRowValidation.ValidateSchema(target);
        ValidatePredicatePaths(query.Filter, request.Parameters);
        QueryFieldAuthorization.Validate(database, principal, model, request);
        database.Authorization.RequireFieldUse(principal, model, ReferencePath);
        database.Authorization.RequireFieldUse(principal, target, JsonData.Path([schema.PrimaryKey]));
        foreach (var selection in query.Projection)
        {
            if (selection.SourceAlias == query.Alias && IsEventPath(selection.Path))
            {
                continue;
            }
            var segments = JsonData.PathSegments(selection.Path);
            if (selection.SourceAlias == join.Alias && segments.Length == SinglePath
                && schema.Columns.Any(column => column.Name == segments[First]))
            {
                continue;
            }
            throw Errors.Fail(ErrorCode.UnsupportedCapability, Invalid);
        }
        budget.CaptureResourceLifetimes(database, budget.CreateView(view), request.Partition, [streamSet, target]);
        return target;
    }

    private static bool IsEventPath(string path)
    {
        var parts = JsonData.PathSegments(path);
        return parts.Length > First && EventFields.Contains(parts[First], StringComparer.Ordinal)
            && (parts.Length == SinglePath || parts[First] is PayloadField or HeadersField);
    }
}
