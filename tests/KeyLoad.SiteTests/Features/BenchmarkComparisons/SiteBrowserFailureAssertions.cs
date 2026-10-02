namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteBrowserFailureAssertions
{
    public static async Task AssertFailureAndRetry(SiteBrowserSession browser, CancellationToken cancellationToken)
    {
        var catalogPath = Path.Combine(browser.Output, SiteTokens.DataDirectory, SiteTokens.CatalogFile);
        var catalog = await File.ReadAllBytesAsync(catalogPath, cancellationToken);
        try
        {
            await File.WriteAllTextAsync(catalogPath, SiteTokens.InvalidJson, cancellationToken);
            await browser.Chrome.NavigateAsync(browser.BaseUrl + SiteAssetTokens.IndexHtml, cancellationToken);
            await WaitForFailure(browser.Chrome.Cdp, cancellationToken);
            await AssertClearedFailure(browser.Chrome.Cdp, cancellationToken);
        }
        finally
        {
            await File.WriteAllBytesAsync(catalogPath, catalog, cancellationToken);
        }

        await Retry(browser.Chrome.Cdp, cancellationToken);
        await SiteBrowserAssertions.WaitForLoaded(browser.Chrome.Cdp, cancellationToken);
        await AssertChartReady(browser.Chrome.Cdp, cancellationToken);
        await AssertReportHashFailure(browser, cancellationToken);
        await Retry(browser.Chrome.Cdp, cancellationToken);
        await SiteBrowserAssertions.WaitForLoaded(browser.Chrome.Cdp, cancellationToken);
        await AssertChartReady(browser.Chrome.Cdp, cancellationToken);
    }

    public static async Task AssertRapidProfileSwitch(SiteBrowserSession browser, SiteTestInputs inputs,
        CancellationToken cancellationToken)
    {
        await SiteBrowserAssertions.ClickScenario(browser.Chrome.Cdp, SiteTokens.PointRead, cancellationToken);
        await SiteBrowserAssertions.SelectValue(browser.Chrome.Cdp, SiteBrowserUiTokens.SelectorMetric,
            SiteTokens.ThroughputMetric, cancellationToken);
        await SiteBrowserAssertions.SelectValue(browser.Chrome.Cdp, SiteBrowserUiTokens.SelectorRepetition,
            SiteTokens.MedianRepetition, cancellationToken);
        await SiteBrowserAssertions.SelectValue(browser.Chrome.Cdp, SiteBrowserUiTokens.SelectorProfile,
            SiteTokens.LargeProfile, cancellationToken);
        await SiteBrowserAssertions.SelectValue(browser.Chrome.Cdp, SiteBrowserUiTokens.SelectorProfile,
            SiteTokens.SmokeProfile, cancellationToken);
        await SiteBrowserAssertions.WaitForLoaded(browser.Chrome.Cdp, cancellationToken);
        await AssertSmokeProfileMatchesIndependentOracle(browser.Chrome.Cdp, inputs, cancellationToken);
    }

    private static async Task AssertSmokeProfileMatchesIndependentOracle(SiteBrowserCdpClient cdp,
        SiteTestInputs inputs, CancellationToken cancellationToken)
    {
        var selected = await cdp.EvaluateAsync(SiteBrowserBehaviorTokens.SelectedProfileScript, false, cancellationToken);
        await Assert.That(selected.GetString()).IsEqualTo(SiteTokens.SmokeProfile);
        var path = Path.Combine(inputs.Reports, SiteTokens.SmokeProfile, SiteTokens.ReportFile);
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var report = System.Text.Json.JsonSerializer.Deserialize(bytes, SiteReportJsonContext.Create().SiteReport)
            ?? throw new System.Text.Json.JsonException();
        var expected = SiteMeasurementOracle.Rows(report, SiteTokens.PointRead, SiteTokens.ThroughputMetric,
            SiteTokens.MedianRepetition);
        var snapshot = await SiteBrowserAssertions.ReadSnapshot(cdp, cancellationToken);
        await SiteBrowserAssertions.AssertSnapshot(snapshot, expected);
        var revision = await cdp.EvaluateAsync(SiteBrowserBehaviorTokens.ProvenanceScript, false, cancellationToken);
        await Assert.That(revision.GetString()!.Contains(inputs.MeasuredRevision[..SiteBrowserUiTokens.RevisionPrefixLength],
            StringComparison.Ordinal)).IsTrue();
    }

    private static async Task AssertReportHashFailure(SiteBrowserSession browser, CancellationToken cancellationToken)
    {
        var reportPath = Path.Combine(browser.Output, SiteTokens.DataDirectory, SiteTokens.RunsDirectory,
            SiteTokens.SmokeProfile, SiteTokens.ReportFile);
        var rawReport = await File.ReadAllBytesAsync(reportPath, cancellationToken);
        try
        {
            var changed = new byte[rawReport.Length + SiteTokens.One];
            changed[SiteTokens.Zero] = SiteTokens.JsonLeadingWhitespace;
            rawReport.CopyTo(changed, SiteTokens.One);
            await File.WriteAllBytesAsync(reportPath, changed, cancellationToken);
            await SiteBrowserAssertions.SelectValue(browser.Chrome.Cdp, SiteBrowserUiTokens.SelectorProfile,
                SiteTokens.SmallProfile, cancellationToken);
            await SiteBrowserAssertions.SelectValue(browser.Chrome.Cdp, SiteBrowserUiTokens.SelectorProfile,
                SiteTokens.SmokeProfile, cancellationToken);
            await WaitForFailure(browser.Chrome.Cdp, cancellationToken);
            await AssertClearedFailure(browser.Chrome.Cdp, cancellationToken);
        }
        finally
        {
            await File.WriteAllBytesAsync(reportPath, rawReport, cancellationToken);
        }
    }

    private static async Task WaitForFailure(SiteBrowserCdpClient cdp, CancellationToken cancellationToken)
    {
        var reached = await cdp.WaitForExpressionAsync(SiteBrowserUiTokens.WaitFailureScript, cancellationToken);
        await Assert.That(reached).IsTrue();
    }

    private static async Task AssertClearedFailure(SiteBrowserCdpClient cdp, CancellationToken cancellationToken)
    {
        var state = await cdp.EvaluateAsync(SiteBrowserUiTokens.FailureStateScript, false, cancellationToken);
        await Assert.That(state.GetProperty(SiteBrowserBehaviorTokens.VisibleField).GetBoolean()).IsTrue();
        await Assert.That(state.GetProperty(SiteBrowserBehaviorTokens.RetryVisibleField).GetBoolean()).IsTrue();
        await Assert.That(state.GetProperty(SiteBrowserBehaviorTokens.RowCountField).GetInt32()).IsEqualTo(SiteTokens.Zero);
        await Assert.That(state.GetProperty(SiteBrowserUiTokens.DownloadsField).EnumerateArray().All(item => !item.GetBoolean())).IsTrue();
        await Assert.That(state.GetProperty(SiteBrowserUiTokens.RevisionField).GetString()).IsEqualTo(SiteBrowserBehaviorTokens.EmptyText);
    }

    private static async Task Retry(SiteBrowserCdpClient cdp, CancellationToken cancellationToken)
    {
        await SiteBrowserAssertions.EvaluateSuccess(cdp, SiteBrowserUiTokens.RetryFailureScript, cancellationToken);
    }

    private static async Task AssertChartReady(SiteBrowserCdpClient cdp, CancellationToken cancellationToken)
    {
        var ready = await cdp.WaitForExpressionAsync(SiteBrowserBehaviorTokens.ChartReadyScript, cancellationToken);
        await Assert.That(ready).IsTrue();
    }
}
