namespace KeyLoad.Server;

internal sealed class AdminHttpMetricsMiddleware(RequestDelegate next, TimeProvider? clock = null)
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;

    private const string CommandsPrefix = ApiRoutes.Commands;
    private const string DocumentsPrefix = "/documents";
    private const string StreamsPrefix = "/streams";
    private const string EventsPrefix = "/events";
    private const string SubscriptionsPrefix = "/subscriptions";
    private const string QueuesPrefix = "/queues";
    private const string InboxPrefix = "/inbox";
    private const string GraphPrefix = "/graph";
    private const string SearchPrefix = ApiRoutes.Search;
    private const string SeriesPrefix = "/series";
    private const string QueryPrefix = ApiRoutes.Query;
    private const string AdminResourcesPrefix = ApiRoutes.ResourcesConfigure;
    private const string AdminPrincipalsPrefix = ApiRoutes.PrincipalsConfigure;
    private const string AdminApiKeysPrefix = ApiRoutes.ApiKeysConfigure;
    private const string AdminOutboxPrefix = "/admin/outbox";
    private const string AdminProjectionsPrefix = "/admin/projections";
    private const string AdminDispatchPrefix = ApiRoutes.AdminDispatch;
    private const string AdminBackupPrefix = ApiRoutes.AdminBackup;
    private const string AdminTablesPrefix = "/admin/tables";
    private const string ChangesPrefix = "/changes";
    private const string AnalyticsPrefix = "/analytics";

    private static readonly string[] ApiPrefixes =
    [
        CommandsPrefix, DocumentsPrefix, StreamsPrefix, EventsPrefix, SubscriptionsPrefix, QueuesPrefix, InboxPrefix,
        GraphPrefix, SearchPrefix, SeriesPrefix, QueryPrefix, AdminResourcesPrefix, AdminPrincipalsPrefix, AdminApiKeysPrefix,
        AdminOutboxPrefix, AdminProjectionsPrefix, AdminDispatchPrefix, AdminBackupPrefix, AdminTablesPrefix, ChangesPrefix, AnalyticsPrefix
    ];
    private const string McpPrefix = "/mcp";
    private const string StatusPath = ApiRoutes.Status;
    private const int ErrorStatus = 400;

    private static readonly string[] RecognizedMethods =
    [
        HttpMethods.Get, HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch,
        HttpMethods.Delete, HttpMethods.Head, HttpMethods.Options
    ];

    public async Task InvokeAsync(HttpContext context, AdminHttpMetrics metrics)
    {
        if (!Included(context.Request.Path))
        {
            await next(context);
            return;
        }
        var started = time.GetTimestamp();
        var faulted = true;
        try
        {
            await next(context);
            faulted = false;
        }
        finally
        {
            metrics.Record(time.GetElapsedTime(started), Describe(context, faulted));
        }
    }

    internal static bool Included(PathString path) =>
        (ApiPrefixes.Any(prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase)) || path.StartsWithSegments(McpPrefix, StringComparison.OrdinalIgnoreCase))
        && !path.StartsWithSegments(AdminDashboardProtocol.SnapshotPath, StringComparison.OrdinalIgnoreCase)
        && !path.Equals(new PathString(StatusPath), StringComparison.OrdinalIgnoreCase);

    private static AdminHttpFailureDetail? Describe(HttpContext context, bool faulted)
    {
        var aborted = context.RequestAborted.IsCancellationRequested;
        var status = faulted && context.Response.StatusCode < ErrorStatus
            ? StatusCodes.Status500InternalServerError
            : context.Response.StatusCode;
        return faulted || aborted || status >= ErrorStatus
            ? new AdminHttpFailureDetail(NormalizeMethod(context.Request.Method), RouteTemplate(context), status, aborted)
            : null;
    }

    private static string NormalizeMethod(string method)
    {
        foreach (var recognized in RecognizedMethods)
        {
            if (HttpMethods.Equals(method, recognized))
            {
                return recognized;
            }
        }

        return AdminDashboardProtocol.OtherMethod;
    }

    private static string RouteTemplate(HttpContext context) =>
        context.GetEndpoint() is RouteEndpoint { RoutePattern.RawText: { Length: > 0 } template }
            ? template
            : AdminDashboardProtocol.UnmatchedRoute;
}
