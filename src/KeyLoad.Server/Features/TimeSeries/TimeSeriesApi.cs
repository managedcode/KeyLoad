namespace KeyLoad.Server;

internal static class TimeSeriesApi
{
    private const string ReadPath = "/v1/series/read";

    internal static void Map(WebApplication app)
    {
        app.MapPost(ReadPath, (ReadSamplesRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Orleans.GrainReadKind.Samples, request));
        app.MapPost(TimeSeriesReadProtocol.LatestRoute, (ReadLatestSampleRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Orleans.GrainReadKind.LatestSample, request));
        app.MapPost(TimeSeriesReadProtocol.AggregateRoute, (AggregateSamplesRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Orleans.GrainReadKind.AggregateSamples, request));
        app.MapPost(TimeSeriesReadProtocol.WindowsRoute, (AggregateSampleWindowsRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Orleans.GrainReadKind.AggregateSampleWindows, request));
    }
}
