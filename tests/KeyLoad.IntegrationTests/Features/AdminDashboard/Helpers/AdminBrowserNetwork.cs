using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.AdminDashboard;

internal sealed class AdminBrowserNetwork
{
    private const string MethodProperty = "method";
    private const string ParamsProperty = "params";
    private const string RequestProperty = "request";
    private const string UrlProperty = "url";
    private const string RequestIdProperty = "requestId";
    private const string RequestStarted = "Network.requestWillBeSent";
    private const string RequestFinished = "Network.loadingFinished";
    private const string RequestFailed = "Network.loadingFailed";
    private const string ApiPath = "/v1/";
    private const int MaximumTrackedRequests = 256;
    private readonly HashSet<string> active = new(StringComparer.Ordinal);

    internal int MaximumConcurrent { get; private set; }
    internal int ActiveRequests => active.Count;

    internal void Observe(JsonElement message)
    {
        if (!message.TryGetProperty(MethodProperty, out var method)
            || !message.TryGetProperty(ParamsProperty, out var arguments)
            || !arguments.TryGetProperty(RequestIdProperty, out var identifier))
        { return; }
        var id = identifier.GetString()!;
        if (method.GetString() == RequestStarted)
        {
            var url = arguments.GetProperty(RequestProperty).GetProperty(UrlProperty).GetString();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var target)
                || !target.AbsolutePath.StartsWith(ApiPath, StringComparison.Ordinal))
            { return; }
            if (active.Count >= MaximumTrackedRequests)
            { throw new InvalidOperationException(AdminBrowserProtocol.BrowserFailure); }
            active.Add(id);
            MaximumConcurrent = Math.Max(MaximumConcurrent, active.Count);
        }
        else if (method.GetString() is RequestFinished or RequestFailed)
        { active.Remove(id); }
    }
}
