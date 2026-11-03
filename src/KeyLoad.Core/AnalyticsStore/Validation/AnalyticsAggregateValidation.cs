namespace KeyLoad.Core.AnalyticsStore;

internal static class AnalyticsAggregateValidation
{
    internal static void Require(DatabaseEngine database, AnalyticsStoreReadScope scope, AggregateAnalyticsRequest request)
    {
        AnalyticsStoreValidation.Range(request.FromInclusive, request.ToExclusive);
        if (request.GroupBy.IsDefault || request.GroupBy.Any(string.IsNullOrWhiteSpace)
            || request.GroupBy.Distinct(StringComparer.Ordinal).Count() != request.GroupBy.Length
            || request.Metrics.IsDefaultOrEmpty || request.Metrics.Any(value => value is null)
            || request.Metrics.Select(value => value.Name).Distinct(StringComparer.Ordinal).Count() != request.Metrics.Length
            || request.MaxGroups <= 0)
        {
            throw Errors.Fail(ErrorCode.Validation, AnalyticsStoreValidation.Invalid);
        }

        var schema = scope.Resource.AnalyticsSchema!;
        foreach (var name in request.GroupBy)
        {
            RequireDimension(database, scope, schema, name);
        }
        if (request.DimensionMatch is { } match)
        {
            if (string.IsNullOrWhiteSpace(match.Dimension) || match.Value is null)
            {
                throw Errors.Fail(ErrorCode.Validation, AnalyticsStoreValidation.Invalid);
            }

            RequireDimension(database, scope, schema, match.Dimension);
            _ = AnalyticsStoreDimensionValues.Require(match.Value);
        }
        foreach (var metric in request.Metrics)
        {
            JsonData.Identifier(metric.Name);
            if (!Enum.IsDefined(metric.Operation))
            {
                throw Errors.Fail(ErrorCode.Validation, AnalyticsStoreValidation.Invalid);
            }

            if (metric.Operation == AnalyticsAggregateKind.Count)
            {
                if (metric.Metric is null || metric.Metric.Length != 0)
                {
                    throw Errors.Fail(ErrorCode.Validation, AnalyticsStoreValidation.Invalid);
                }

                continue;
            }
            if (string.IsNullOrWhiteSpace(metric.Metric))
            {
                throw Errors.Fail(ErrorCode.Validation, AnalyticsStoreValidation.Invalid);
            }

            AnalyticsStoreAuthorization.Field(database, scope.Principal, scope.Resource,
                AnalyticsStoreAuthorization.Path(AnalyticsStoreValidation.Metrics, metric.Metric), false);
            if (!schema.Metrics.Contains(metric.Metric, StringComparer.Ordinal))
            {
                throw Errors.Fail(ErrorCode.Validation, AnalyticsStoreValidation.Invalid);
            }
        }
    }

    private static void RequireDimension(DatabaseEngine database, AnalyticsStoreReadScope scope,
        AnalyticsStoreSchema schema, string name)
    {
        AnalyticsStoreAuthorization.Field(database, scope.Principal, scope.Resource,
            AnalyticsStoreAuthorization.Path(AnalyticsStoreValidation.Dimensions, name), false);
        if (!schema.Dimensions.Contains(name, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Validation, AnalyticsStoreValidation.Invalid);
        }
    }
}
