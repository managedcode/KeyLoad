namespace KeyLoad.Server;

internal sealed class AdminHttpMetricsMiddleware(RequestDelegate next, TimeProvider? clock = null)
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private const int RouteTemplateGetEndpointEmptyCount = 0;

    private const string CommandsPrefix = "/commands";
    private const string DocumentsPrefix = "/documents";
    private const string StreamsPrefix = "/streams";
    private const string EventsPrefix = "/events";
    private const string SubscriptionsPrefix = "/subscriptions";
    private const string QueuesPrefix = "/queues";
    private const string InboxPrefix = "/inbox";
    private const string GraphPrefix = "/graph";
    private const string SearchPrefix = "/search";
    private const string SeriesPrefix = "/series";
    private const string QueryPrefix = "/query";
    private const string AdminResourcesPrefix = "/admin/resources";
    private const string AdminPrincipalsPrefix = "/admin/principals";
    private const string AdminApiKeysPrefix = "/admin/api-keys";
    private const string AdminOutboxPrefix = "/admin/outbox";
    private const string AdminProjectionsPrefix = "/admin/projections";
    private const string AdminDispatchPrefix = "/admin/dispatch";
    private const string AdminBackupPrefix = "/admin/backup";
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
    private const string AdmissionPath = "/admin/admission";
    private const string StatusPath = "/status";
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
        && !path.Equals(new PathString(AdmissionPath), StringComparison.OrdinalIgnoreCase)
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
        context.GetEndpoint() is RouteEndpoint { RoutePattern.RawText: { Length: > RouteTemplateGetEndpointEmptyCount } template }
            ? template
            : AdminDashboardProtocol.UnmatchedRoute;
}
