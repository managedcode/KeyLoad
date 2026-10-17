using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteBrowserTarget
{
    public static async Task<Uri> WaitForEndpointFile(Process process, string profilePath,
        CancellationToken cancellationToken)
    {
        using var deadlineTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(SiteBrowserTokens.BrowserStartupTimeoutMilliseconds), TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadlineTimeout.Token);
        var portFile = Path.Combine(profilePath, SiteBrowserTokens.ActivePortFile);
        while (!deadline.IsCancellationRequested)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException(SiteBrowserTokens.BrowserStartFailure);
            }
            if (File.Exists(portFile))
            {
                var lines = await File.ReadAllLinesAsync(portFile, deadline.Token);
                if (lines.Length > SiteBrowserTokens.Zero && int.TryParse(lines[SiteBrowserTokens.Zero],
                    NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port > SiteBrowserTokens.Zero)
                {
                    return new UriBuilder(SiteBrowserTokens.HttpScheme, SiteBrowserTokens.LoopbackHost, port).Uri;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(SiteBrowserTokens.BrowserRetryMilliseconds), TimeProvider.System, deadline.Token);
        }

        throw new InvalidOperationException(SiteBrowserTokens.BrowserStartupTimeout);
    }

    public static async Task<SiteBrowserCdpClient> ConnectPage(HttpClient http, Uri baseUri,
        CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(new Uri(baseUri, SiteBrowserTokens.JsonListEndpoint), cancellationToken);
        response.EnsureSuccessStatusCode();
        using var targets = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        var page = targets.RootElement.EnumerateArray().FirstOrDefault(target =>
            target.GetProperty(SiteBrowserTokens.TargetTypeField).GetString() == SiteBrowserTokens.TargetPage);
        if (page.ValueKind == JsonValueKind.Undefined)
        {
            using var created = await http.PutAsync(new Uri(baseUri, SiteBrowserTokens.JsonNewEndpoint), null, cancellationToken);
            created.EnsureSuccessStatusCode();
            using var target = await JsonDocument.ParseAsync(await created.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            return await ConnectTarget(target.RootElement, cancellationToken);
        }

        return await ConnectTarget(page, cancellationToken);
    }

    private static Task<SiteBrowserCdpClient> ConnectTarget(JsonElement target, CancellationToken cancellationToken)
    {
        var endpoint = new Uri(target.GetProperty(SiteBrowserTokens.WebSocketUrlField).GetString()!);
        return SiteBrowserCdpClient.ConnectAsync(endpoint, cancellationToken);
    }
}
