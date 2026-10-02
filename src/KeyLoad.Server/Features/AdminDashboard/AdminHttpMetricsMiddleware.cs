using System.Diagnostics;

namespace KeyLoad.Server;

internal sealed class AdminHttpMetricsMiddleware(RequestDelegate next)
{
    private const string ApiPrefix = "/v1";
    private const string McpPrefix = "/mcp";
    private const string AdmissionPath = "/v1/admin/admission";
    private const string StatusPath = "/v1/status";
    private const int ErrorStatus = 400;

    public async Task InvokeAsync(HttpContext context, AdminHttpMetrics metrics)
    {
        if (!Included(context.Request.Path))
        { await next(context).ConfigureAwait(false); return; }
        var started = Stopwatch.GetTimestamp();
        var failed = true;
        try
        {
            await next(context).ConfigureAwait(false);
            failed = context.Response.StatusCode >= ErrorStatus || context.RequestAborted.IsCancellationRequested;
        }
        finally
        { metrics.Record(Stopwatch.GetElapsedTime(started), failed); }
    }

    internal static bool Included(PathString path) =>
        (path.StartsWithSegments(ApiPrefix) || path.StartsWithSegments(McpPrefix))
        && !path.StartsWithSegments(AdminDashboardProtocol.SnapshotPath)
        && !path.Equals(new PathString(AdmissionPath)) && !path.Equals(new PathString(StatusPath));
}
