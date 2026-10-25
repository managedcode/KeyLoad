using System.Collections.Immutable;

namespace KeyLoad.Core.AnalyticsStore;

internal static class AnalyticsStoreValidation
{
    private const int FieldPathSegments = 2;
    internal const string Invalid = "The analytics store request is invalid.";
    internal const string UnavailableBinding = "The requested field binding is unavailable.";
    internal const string Budget = "The analytics store work budget is exceeded.";

    internal static void ValidateSchema(ResourceDefinition definition)
    {
        if (definition.Kind != ResourceKind.AnalyticsStore)
        {
            if (definition.AnalyticsSchema is not null)
            {
                throw Errors.Fail(ErrorCode.Validation, Invalid);
            }

            return;
        }
        var schema = definition.AnalyticsSchema;
        if (schema is null || !Names(schema.Dimensions) || !Names(schema.Metrics)
            || definition.Indexes.Length != 0 || definition.HeaderPolicies.Length != 0
            || definition.Authority != DocumentAuthority.Document || definition.RelationalSchema is not null)
        {
            throw Errors.Fail(ErrorCode.Validation, Invalid);
        }

        foreach (var policy in definition.FieldPolicies)
        {
            var path = JsonData.PathSegments(policy.Path);
            if (path.Length == 0)
            {
                continue;
            }

            if (path[0] == Links && path.Length == 1)
            {
                continue;
            }

            var fields = path[0] == Dimensions ? schema.Dimensions : path[0] == Metrics ? schema.Metrics : default;
            if (fields.IsDefault || path.Length > FieldPathSegments || path.Length == FieldPathSegments && !fields.Contains(path[1], StringComparer.Ordinal))
            {
                throw Errors.Fail(ErrorCode.Validation, Invalid);
            }
        }
    }

    internal const string Dimensions = "dimensions";
    internal const string Metrics = "metrics";
    internal const string Links = "links";
    internal static bool Names(ImmutableArray<string> names)
    {
        if (names.IsDefault || names.Any(string.IsNullOrWhiteSpace))
        {
            return false;
        }

        foreach (var name in names)
        {
            JsonData.Identifier(name);
        }

        return names.SequenceEqual(names.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
    }

    internal static void Event(AnalyticsEventData value, AnalyticsStoreSchema schema)
    {
        if (value is null || value.Dimensions is null || value.Metrics is null || value.Links.IsDefault
            || value.Timestamp.Offset != TimeSpan.Zero || value.Dimensions.Any(item => item.Value is null)
            || value.Metrics.Any(item => !double.IsFinite(item.Value))
            || !value.Dimensions.Keys.Order(StringComparer.Ordinal).SequenceEqual(schema.Dimensions)
            || !value.Metrics.Keys.Order(StringComparer.Ordinal).SequenceEqual(schema.Metrics))
        {
            throw Errors.Fail(ErrorCode.Validation, Invalid);
        }

        foreach (var dimension in value.Dimensions.Values)
        {
            _ = AnalyticsStoreDimensionValues.Require(dimension);
        }

        JsonData.Identifier(value.Id);
    }

    internal static void Range(DateTimeOffset from, DateTimeOffset to)
    {
        if (from.Offset != TimeSpan.Zero || to.Offset != TimeSpan.Zero || from >= to)
        {
            throw Errors.Fail(ErrorCode.Validation, Invalid);
        }
    }
}
