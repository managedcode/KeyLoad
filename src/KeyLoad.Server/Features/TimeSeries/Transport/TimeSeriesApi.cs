namespace KeyLoad.Server;

internal static class TimeSeriesApi
{
    private const string ReadPath = "/v1/series/read";

    internal static void Map(WebApplication app)
    {
        app.MapPost(ReadPath, (ReadSamplesRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Clustering.GrainReadKind.Samples, request));
        app.MapPost(TimeSeriesReadProtocol.LatestRoute, (ReadLatestSampleRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Clustering.GrainReadKind.LatestSample, request));
        app.MapPost(TimeSeriesReadProtocol.AggregateRoute, (AggregateSamplesRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Clustering.GrainReadKind.AggregateSamples, request));
        app.MapPost(TimeSeriesReadProtocol.WindowsRoute, (AggregateSampleWindowsRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Clustering.GrainReadKind.AggregateSampleWindows, request));
        app.MapPost(SampleChunkProtocol.ReadRoute, (ReadSampleChunkWindowRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Clustering.GrainReadKind.SampleChunkWindow, request));
        app.MapPost(SampleRollupProtocol.ReadRoute, (ReadSampleRollupRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Clustering.GrainReadKind.SampleRollup, request));
        app.MapPost(TimeSeriesReadProtocol.RetentionRoute, (ReadSampleRetentionRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Clustering.GrainReadKind.SampleRetention, request));
    }
}
