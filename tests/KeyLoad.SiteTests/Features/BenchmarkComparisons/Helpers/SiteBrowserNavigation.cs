using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteBrowserNavigation
{
    public static async Task WaitForReadyAsync(SiteBrowserCdpClient cdp, string requestedUrl,
        JsonElement navigation, CancellationToken cancellationToken)
    {
        var frameId = navigation.GetProperty(SiteBrowserTokens.TargetFrameIdField).GetString()
            ?? throw new InvalidOperationException(SiteBrowserTokens.BrowserProtocolFailure);
        var loaderId = navigation.TryGetProperty(SiteBrowserTokens.LoaderIdField, out var loader)
            ? loader.GetString() : null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SiteBrowserTokens.BrowserCommandTimeoutMilliseconds);
        while (true)
        {
            var tree = await cdp.CommandAsync(SiteBrowserTokens.PageGetFrameTree, null, timeout.Token);
            var frame = tree.GetProperty(SiteBrowserTokens.FrameTreeField).GetProperty(SiteBrowserTokens.FrameField);
            if (FrameMatches(frame, frameId, loaderId, requestedUrl))
            {
                var script = SiteBrowserTokens.NavigationReadyScriptPrefix + JsonSerializer.Serialize(requestedUrl);
                var ready = await cdp.EvaluateAsync(script, awaitPromise: false, timeout.Token);
                if (ready.ValueKind == JsonValueKind.True)
                {
                    return;
                }
            }

            await Task.Delay(SiteBrowserTokens.NavigationPollDelayMilliseconds, timeout.Token);
        }
    }

    private static bool FrameMatches(JsonElement frame, string frameId, string? loaderId, string requestedUrl) =>
        frame.GetProperty(SiteBrowserTokens.FrameIdField).GetString() == frameId &&
        FrameUrlMatches(frame, requestedUrl) &&
        (loaderId is null || frame.TryGetProperty(SiteBrowserTokens.LoaderIdField, out var currentLoader) &&
            currentLoader.GetString() == loaderId);

    private static bool FrameUrlMatches(JsonElement frame, string requestedUrl)
    {
        var url = frame.GetProperty(SiteBrowserTokens.FrameUrlField).GetString();
        var fragment = frame.TryGetProperty(SiteBrowserTokens.FrameUrlFragmentField, out var value)
            ? value.GetString() : null;
        return string.Concat(url, fragment) == requestedUrl;
    }
}
