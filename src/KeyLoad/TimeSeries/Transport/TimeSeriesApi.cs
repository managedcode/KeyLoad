namespace KeyLoad.Server;

internal static class TimeSeriesApi
{
    private const string ReadPath = ApiRoutes.SeriesRead;

    internal static void Map(WebApplication app)
    {
        app.MapPost(ReadPath, (ReadSamplesRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Clustering.GrainReadKind.Samples, request))
            .RequireAuthorization(DatabaseAuthenticationComposition.SubjectPolicy);
        app.MapPost(TimeSeriesReadProtocol.LatestRoute, (ReadLatestSampleRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Clustering.GrainReadKind.LatestSample, request))
            .RequireAuthorization(DatabaseAuthenticationComposition.SubjectPolicy);
        app.MapPost(TimeSeriesReadProtocol.AggregateRoute, (AggregateSamplesRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Clustering.GrainReadKind.AggregateSamples, request))
            .RequireAuthorization(DatabaseAuthenticationComposition.SubjectPolicy);
        app.MapPost(TimeSeriesReadProtocol.WindowsRoute, (AggregateSampleWindowsRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Clustering.GrainReadKind.AggregateSampleWindows, request))
            .RequireAuthorization(DatabaseAuthenticationComposition.SubjectPolicy);
        app.MapPost(SampleChunkProtocol.ReadRoute, (ReadSampleChunkWindowRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Clustering.GrainReadKind.SampleChunkWindow, request))
            .RequireAuthorization(DatabaseAuthenticationComposition.SubjectPolicy);
        app.MapPost(SampleRollupProtocol.ReadRoute, (ReadSampleRollupRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Clustering.GrainReadKind.SampleRollup, request))
            .RequireAuthorization(DatabaseAuthenticationComposition.SubjectPolicy);
        app.MapPost(TimeSeriesReadProtocol.RetentionRoute, (ReadSampleRetentionRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Clustering.GrainReadKind.SampleRetention, request))
            .RequireAuthorization(DatabaseAuthenticationComposition.SubjectPolicy);
    }
}
