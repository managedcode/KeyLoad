namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteBrowserNoScriptAssertions
{
    public static async Task AssertNoScript(SiteBrowserChrome chrome, string baseUrl, SiteTestInputs inputs,
        CancellationToken cancellationToken)
    {
        await chrome.Cdp.CommandAsync(SiteBrowserTokens.DomEnable, null, cancellationToken);
        await chrome.Cdp.CommandAsync(SiteBrowserTokens.SetScriptExecutionDisabled, new Dictionary<string, object?>
        {
            [SiteBrowserTokens.ValueField] = true,
        }, cancellationToken);
        await chrome.NavigateAsync(baseUrl + SiteAssetTokens.IndexHtml, cancellationToken);
        var nodeId = await WaitForNoScriptNode(chrome.Cdp, cancellationToken);
        var outer = await chrome.Cdp.CommandAsync(SiteBrowserTokens.GetOuterHtml, new Dictionary<string, object?>
        {
            [SiteBrowserTokens.NodeIdField] = nodeId,
        }, cancellationToken);
        var html = outer.GetProperty(SiteBrowserUiTokens.HtmlField).GetString() ?? SiteBrowserBehaviorTokens.EmptyText;
        foreach (var profile in SiteTokens.ProfileNames)
        {
            var path = SiteBrowserUiTokens.NoScriptReportDirectory + profile + SiteBrowserUiTokens.NoScriptReportSuffix;
            await Assert.That(html.Contains(path, StringComparison.Ordinal)).IsTrue();
        }
        await Assert.That(html.Contains(inputs.EvidenceUrl, StringComparison.Ordinal)).IsTrue();
        await Assert.That(html.Contains(SiteBrowserUiTokens.NoScriptEvidenceLabel, StringComparison.OrdinalIgnoreCase)).IsTrue();
    }

    private static async Task<int> WaitForNoScriptNode(SiteBrowserCdpClient cdp, CancellationToken cancellationToken)
    {
        for (var attempt = SiteTokens.Zero; attempt < SiteBrowserUiTokens.NoScriptWaitAttempts; attempt++)
        {
            var document = await cdp.CommandAsync(SiteBrowserTokens.GetDocument, new Dictionary<string, object?>
            {
                [SiteBrowserTokens.DepthField] = SiteBrowserTokens.One,
            }, cancellationToken);
            var root = document.GetProperty(SiteBrowserTokens.RootNodeField)
                .GetProperty(SiteBrowserTokens.NodeIdField).GetInt32();
            var selected = await cdp.CommandAsync(SiteBrowserTokens.QuerySelector,
                new Dictionary<string, object?>
                {
                    [SiteBrowserTokens.NodeIdField] = root,
                    [SiteBrowserTokens.SelectorField] = SiteBrowserUiTokens.NoScriptSelector,
                }, cancellationToken);
            var nodeId = selected.GetProperty(SiteBrowserTokens.NodeIdField).GetInt32();
            if (nodeId > SiteBrowserTokens.Zero)
            {
                return nodeId;
            }
            await Task.Delay(SiteBrowserUiTokens.NoScriptPollMilliseconds, cancellationToken);
        }

        throw new InvalidOperationException(SiteBrowserTokens.BrowserNoScriptMissing);
    }
}
