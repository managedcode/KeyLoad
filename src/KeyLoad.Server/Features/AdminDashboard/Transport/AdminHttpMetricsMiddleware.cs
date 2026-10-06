namespace KeyLoad.Server;

internal sealed class AdminHttpMetricsMiddleware(RequestDelegate next, TimeProvider? clock = null)
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private const int RouteTemplateGetEndpointEmptyCount = 0;

    private const string ApiPrefix = "/v1";
    private const string McpPrefix = "/mcp";
    private const string AdmissionPath = "/v1/admin/admission";
    private const string StatusPath = "/v1/status";
    private const int ErrorStatus = 400;

    private static readonly string[] RecognizedMethods =
    [
        HttpMethods.Get, HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch,
        HttpMethods.Delete, HttpMethods.Head, HttpMethods.Options
    ];

    public async Task InvokeAsync(HttpContext context, AdminHttpMetrics metrics)
    {
        if (!Included(context.Request.Path))
        { await next(context).ConfigureAwait(false); return; }
        var started = time.GetTimestamp();
        var faulted = true;
        try
        {
            await next(context).ConfigureAwait(false);
            faulted = false;
        }
        finally
        { metrics.Record(time.GetElapsedTime(started), Describe(context, faulted)); }
    }

    internal static bool Included(PathString path) =>
        (path.StartsWithSegments(ApiPrefix, StringComparison.OrdinalIgnoreCase) || path.StartsWithSegments(McpPrefix, StringComparison.OrdinalIgnoreCase))
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
            { return recognized; }
        }

        return AdminDashboardProtocol.OtherMethod;
    }

    private static string RouteTemplate(HttpContext context) =>
        context.GetEndpoint() is RouteEndpoint { RoutePattern.RawText: { Length: > RouteTemplateGetEndpointEmptyCount } template }
            ? template
            : AdminDashboardProtocol.UnmatchedRoute;
}
