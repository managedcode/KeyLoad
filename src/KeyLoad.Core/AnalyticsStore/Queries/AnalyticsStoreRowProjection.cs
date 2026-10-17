using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.AnalyticsStore;

internal static class AnalyticsStoreRowProjection
{
    internal static AnalyticsEventRecord Project(DatabaseEngine database, IKeyValueView view,
        AnalyticsStoreReadScope scope, PartitionRef partition, AnalyticsEventRecord row)
    {
        var json = JsonSerializer.Serialize(new
        {
            dimensions = row.Dimensions,
            metrics = row.Metrics,
            links = row.Links
        }, JsonDefaults.Options);
        var projected = database.Authorization.Project(scope.Principal, scope.Resource.FieldPolicies, json, out _);
        using var document = JsonDocument.Parse(projected);
        var root = document.RootElement;
        var dimensions = root.TryGetProperty(AnalyticsStoreValidation.Dimensions, out var dimensionValue)
            ? dimensionValue.Deserialize<ImmutableDictionary<string, string>>(JsonDefaults.Options)! : ImmutableDictionary<string, string>.Empty;
        var metrics = root.TryGetProperty(AnalyticsStoreValidation.Metrics, out var metricValue)
            ? metricValue.Deserialize<ImmutableDictionary<string, double>>(JsonDefaults.Options)! : ImmutableDictionary<string, double>.Empty;
        var links = root.TryGetProperty(AnalyticsStoreValidation.Links, out var linkValue)
            ? linkValue.Deserialize<ImmutableArray<EntityRef>>(JsonDefaults.Options) : [];
        AnalyticsStoreLinks.RequireStored(database, view, scope.Principal, partition, links);
        return row with
        {
            Dimensions = dimensions,
            Metrics = metrics,
            Links = links
        };
    }
}
