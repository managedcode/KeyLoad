using KeyLoad.Orleans;

namespace KeyLoad.Server;

/// <summary>Composes version-one typed feature routes through distinct Orleans request actors.</summary>
internal static class ApiEndpoints
{
    private const string AdmissionPath = "/v1/admin/admission";
    private const string BackupPath = "/v1/admin/backup";
    private const string StatusPath = "/v1/status";

    /// <summary>Maps all existing public operations to their owning feature adapters.</summary>
    /// <param name="app">Application whose trusted identity middleware has been configured.</param>
    /// <returns>The same application for continued composition.</returns>
    public static WebApplication MapKeyLoadApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        DocumentApi.Map(app);
        BlobApi.Map(app);
        EventStreamApi.Map(app);
        MessagingApi.Map(app);
        GraphApi.Map(app);
        TimeSeriesApi.Map(app);
        QueryApi.Map(app);
        SearchApi.Map(app);
        ChangeFeedApi.Map(app);
        AuthorizationApi.Map(app);
        AtomicPartitionPlacementApi.Map(app);
        AdminDashboardApi.Map(app);
        app.MapGet(AdmissionPath, (Func<HttpContext, Task<IResult>>)(context =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.Admission)));
        app.MapPost(BackupPath, (Func<HttpContext, Task<IResult>>)(context =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.Backup)));
        app.MapGet(StatusPath, (Func<HttpContext, Task<IResult>>)(context =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.NodeStatus)));
        return app;
    }
}
