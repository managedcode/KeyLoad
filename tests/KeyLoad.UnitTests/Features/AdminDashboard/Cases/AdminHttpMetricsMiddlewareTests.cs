using KeyLoad.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace KeyLoad.UnitTests.Features.AdminDashboard;

internal sealed class AdminHttpMetricsMiddlewareTests
{
    private const string RouteTemplate = "/v1/documents/{documentId}/revisions";
    private const string RoutePath = "/v1/documents/secret-document-42/revisions";
    private const string RawQuery = "?token=private-credential&page=2";
    private const string EndpointName = "documents-revisions";
    private const string UnknownMethod = "BREW";
    private const string LowerPost = "post";
    private const string QueryMarker = "?";
    private const string PathSecret = "secret-document-42";
    private const string QuerySecret = "private-credential";
    private const string DashboardPath = "/v1/admin/dashboard";
    private const string AdmissionPath = "/v1/admin/admission";
    private const string StatusPath = "/v1/status";
    private const string StaticPath = "/admin/app.js";
    private const string McpPath = "/mcp";
    private const int NotFound = StatusCodes.Status404NotFound;

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcVi004FailureRecordsRouteTemplateNeverRawPathOrQuery(bool routedInsideNext)
    {
        var snapshot = await InvokeAsync(HttpMethods.Post, RoutePath, RawQuery, NotFound, Routed(), routedInsideNext);
        var entry = snapshot.RecentFailures.Single();
        await Assert.That(entry.Route).IsEqualTo(RouteTemplate);
        await Assert.That(entry.Route.Contains(PathSecret, StringComparison.Ordinal)).IsFalse();
        await Assert.That(entry.Route.Contains(QueryMarker, StringComparison.Ordinal)).IsFalse();
        await Assert.That(entry.Method).IsEqualTo(HttpMethods.Post);
        await Assert.That(entry.StatusCode).IsEqualTo(NotFound);
        await Assert.That(entry.Aborted).IsFalse();
        await Assert.That(entry.ElapsedMilliseconds).IsGreaterThanOrEqualTo(0d);
        await Assert.That(snapshot.CompletedRequests).IsEqualTo(1);
        await Assert.That(snapshot.FailedRequests).IsEqualTo(1);
    }

    [Test]
    public async Task AcVi004UnmatchedFailureUsesFixedMarkerAndLeaksNothing()
    {
        var snapshot = await InvokeAsync(HttpMethods.Get, RoutePath, RawQuery, NotFound, null, false);
        var entry = snapshot.RecentFailures.Single();
        await Assert.That(entry.Route).IsEqualTo(AdminDashboardProtocol.UnmatchedRoute);
        var text = string.Concat(entry.Method, entry.Route);
        await Assert.That(text.Contains(PathSecret, StringComparison.Ordinal)).IsFalse();
        await Assert.That(text.Contains(QuerySecret, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    [Arguments(UnknownMethod, AdminDashboardProtocol.OtherMethod)]
    [Arguments(LowerPost, "POST")]
    [Arguments("GET", "GET")]
    [Arguments("PUT", "PUT")]
    [Arguments("PATCH", "PATCH")]
    [Arguments("DELETE", "DELETE")]
    [Arguments("HEAD", "HEAD")]
    [Arguments("OPTIONS", "OPTIONS")]
    [Arguments("TRACE", AdminDashboardProtocol.OtherMethod)]
    [Arguments("CONNECT", AdminDashboardProtocol.OtherMethod)]
    public async Task AcVi004MethodIsNormalizedToCanonicalNameOrOtherMarker(string method, string expected)
    {
        var snapshot = await InvokeAsync(method, RoutePath, RawQuery, NotFound, Routed(), false);
        await Assert.That(snapshot.RecentFailures.Single().Method).IsEqualTo(expected);
    }

    [Test]
    public async Task AcVi004AbortedRequestIsLoggedEvenWithSuccessfulStatus()
    {
        var snapshot = await InvokeAsync(HttpMethods.Post, RoutePath, RawQuery, StatusCodes.Status200OK, Routed(), false, aborted: true);
        var entry = snapshot.RecentFailures.Single();
        await Assert.That(entry.Aborted).IsTrue();
        await Assert.That(entry.StatusCode).IsEqualTo(StatusCodes.Status200OK);
        await Assert.That(snapshot.FailedRequests).IsEqualTo(1);
    }

    [Test]
    public async Task AcVi004SuccessfulIncludedRequestIsCountedButNotLogged()
    {
        var snapshot = await InvokeAsync(HttpMethods.Post, RoutePath, RawQuery, StatusCodes.Status200OK, Routed(), false);
        await Assert.That(snapshot.CompletedRequests).IsEqualTo(1);
        await Assert.That(snapshot.FailedRequests).IsEqualTo(0);
        await Assert.That(snapshot.RecentFailures.IsDefaultOrEmpty).IsTrue();
    }

    [Test]
    [Arguments(DashboardPath)]
    [Arguments(AdmissionPath)]
    [Arguments(StatusPath)]
    [Arguments(StaticPath)]
    public async Task AcVi004ExcludedPathsRecordNothingEvenWhenFailing(string path)
    {
        var snapshot = await InvokeAsync(HttpMethods.Get, path, RawQuery, StatusCodes.Status500InternalServerError, Routed(), false);
        await Assert.That(snapshot.CompletedRequests).IsEqualTo(0);
        await Assert.That(snapshot.FailedRequests).IsEqualTo(0);
        await Assert.That(snapshot.RecentFailures.IsDefaultOrEmpty).IsTrue();
    }

    [Test]
    public async Task AcVi004McpFailureIsIncludedWithItsRouteTemplate()
    {
        var snapshot = await InvokeAsync(HttpMethods.Post, McpPath, string.Empty, StatusCodes.Status401Unauthorized, Routed(), false);
        await Assert.That(snapshot.RecentFailures.Single().StatusCode).IsEqualTo(StatusCodes.Status401Unauthorized);
        await Assert.That(snapshot.RecentFailures.Single().Route).IsEqualTo(RouteTemplate);
    }

    [Test]
    public async Task AcVi004UnhandledFaultIsLoggedAsServerErrorAndStillPropagates()
    {
        var metrics = new AdminHttpMetrics();
        var context = CreateContext(HttpMethods.Post, RoutePath, RawQuery);
        var middleware = new AdminHttpMetricsMiddleware(_ => throw new InvalidOperationException());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => middleware.InvokeAsync(context, metrics));
        var snapshot = metrics.Snapshot();
        await Assert.That(snapshot.FailedRequests).IsEqualTo(1);
        await Assert.That(snapshot.RecentFailures.Single().StatusCode).IsEqualTo(StatusCodes.Status500InternalServerError);
    }

    private static RouteEndpoint Routed() => new(static _ => Task.CompletedTask,
        RoutePatternFactory.Parse(RouteTemplate), 0, EndpointMetadataCollection.Empty, EndpointName);

    private static DefaultHttpContext CreateContext(string method, string path, string query)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);
        return context;
    }

    private static async Task<AdminHttpSnapshot> InvokeAsync(string method, string path, string query, int status,
        RouteEndpoint? endpoint, bool routedInsideNext, bool aborted = false)
    {
        var metrics = new AdminHttpMetrics();
        var context = CreateContext(method, path, query);
        if (aborted)
        { context.RequestAborted = new CancellationToken(true); }
        if (!routedInsideNext)
        { context.SetEndpoint(endpoint); }
        var middleware = new AdminHttpMetricsMiddleware(http =>
        {
            if (routedInsideNext)
            { http.SetEndpoint(endpoint); }
            http.Response.StatusCode = status;
            return Task.CompletedTask;
        });
        await middleware.InvokeAsync(context, metrics);
        return metrics.Snapshot();
    }
}
