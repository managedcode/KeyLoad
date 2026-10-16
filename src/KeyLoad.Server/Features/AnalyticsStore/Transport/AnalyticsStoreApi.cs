using KeyLoad.Clustering;

namespace KeyLoad.Server;

internal static class AnalyticsStoreApi
{
    internal static void Map(WebApplication app)
    {
        app.MapPost(AnalyticsStoreProtocol.ReadRoute, (ReadAnalyticsEventsRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.AnalyticsEvents, request));
        app.MapPost(AnalyticsStoreProtocol.AggregateRoute, (AggregateAnalyticsRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.AnalyticsAggregate, request));
    }
}
