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
            || request.MaxGroups <= 0 || request.MaxGroups > database.Limits.MaxResults
            || request.Metrics.Length > database.Limits.MaxResults)
        {
            throw Errors.Fail(ErrorCode.Validation, AnalyticsStoreValidation.Invalid);
        }

        var schema = scope.Resource.AnalyticsSchema!;
        foreach (var name in request.GroupBy)
        {
            AnalyticsStoreAuthorization.Field(database, scope.Principal, scope.Resource,
                AnalyticsStoreAuthorization.Path(AnalyticsStoreValidation.Dimensions, name), false);
            if (!schema.Dimensions.Contains(name, StringComparer.Ordinal))
            {
                throw Errors.Fail(ErrorCode.Validation, AnalyticsStoreValidation.Invalid);
            }
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
}
