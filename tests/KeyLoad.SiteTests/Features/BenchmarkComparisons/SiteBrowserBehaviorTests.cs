using System.Globalization;
using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

/// <summary>Runs the emitted comparison page in the workflow-provided real Chrome browser.</summary>
internal sealed class SiteBrowserBehaviorTests
{
    /// <summary>AC-BC-025: browser display expectations use exact shortest decimals and explicit midpoint rules.</summary>
    [Test]
    public async Task AC_BC_025_DisplayOracleRoundsShortestDecimalAtNamedBoundariesAsync()
    {
        await Assert.That(SiteBrowserAssertions.Display(SiteBrowserUiTokens.DisplayBelowHalfValue))
            .IsEqualTo(SiteBrowserUiTokens.DisplayBelowHalfText);
        await Assert.That(SiteBrowserAssertions.Display(SiteBrowserUiTokens.DisplayHalfValue))
            .IsEqualTo(SiteBrowserUiTokens.DisplayHalfText);
        await Assert.That(SiteBrowserAssertions.Display(SiteBrowserUiTokens.DisplayMillisecondTie))
            .IsEqualTo(SiteBrowserUiTokens.DisplayMillisecondTieText);
        await Assert.That(SiteBrowserAssertions.Display(SiteBrowserUiTokens.DisplayBelowMillisecondTie))
            .IsEqualTo(SiteBrowserUiTokens.DisplayBelowMillisecondTieText);
        await Assert.That(SiteBrowserAssertions.Display(SiteBrowserUiTokens.DisplayZeroValue))
            .IsEqualTo(SiteBrowserUiTokens.DisplayZeroText);
        await Assert.That(SiteBrowserAssertions.Display(SiteBrowserUiTokens.DisplayHalfCentValue))
            .IsEqualTo(SiteBrowserUiTokens.DisplayHalfCentText);
        await Assert.That(SiteBrowserAssertions.Display(SiteBrowserUiTokens.DisplayGroupedValue))
            .IsEqualTo(SiteBrowserUiTokens.DisplayGroupedText);
        await Assert.That(SiteBrowserAssertions.Display(null)).IsEqualTo(SiteBrowserUiTokens.MissingValue);
    }

    /// <summary>Matches all profile, scenario and enabled metric views to the independent C# oracle.</summary>
    [Test]
    public async Task AC_BC_025_RealChromeControlsMatchAuthenticReportsAndRetainNativeCoverage()
    {
        var inputs = SiteTestInputs.Read();
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var browser = await SiteBrowserSession.StartAsync(inputs, cancellationToken);
        var cdp = browser.Chrome.Cdp;
        await SiteBrowserAssertions.WaitForLoaded(cdp, cancellationToken);
        await SiteBrowserAssertions.AssertInitialEvidence(cdp, inputs, cancellationToken);
        await SiteBrowserVisualAssertions.AssertSceneIsLazyBeforeHeroNavigation(cdp, cancellationToken);
        foreach (var profile in SiteTokens.ProfileNames)
        {
            await VerifyProfile(browser, inputs, profile, cancellationToken);
        }
        await SiteBrowserFailureAssertions.AssertFailureAndRetry(browser, cancellationToken);
        await SiteBrowserFailureAssertions.AssertRapidProfileSwitch(browser, inputs, cancellationToken);
        await SiteBrowserAssertions.SelectValue(cdp, SiteBrowserUiTokens.SelectorProfile, SiteTokens.SmokeProfile, cancellationToken);
        await SiteBrowserAssertions.WaitForLoaded(cdp, cancellationToken);
        await SiteBrowserAssertions.AssertKeyboardAndDownloads(browser, inputs, cancellationToken);
        await SiteBrowserVisualAssertions.AssertResponsive(browser.Chrome, cancellationToken);
        await SiteBrowserVisualAssertions.AssertSceneLifecycle(browser.Chrome, browser.BaseUrl, cancellationToken);
        await SiteBrowserNoScriptAssertions.AssertNoScript(browser.Chrome, browser.BaseUrl, inputs, cancellationToken);
        await browser.CompleteAsync(cancellationToken);
        await SiteBrowserCoverageAssertions.AssertNativeConversionAsync(browser.BaseUrl, inputs, cancellationToken);
    }

    private static async Task VerifyProfile(SiteBrowserSession browser, SiteTestInputs inputs, string profile,
        CancellationToken cancellationToken)
    {
        var cdp = browser.Chrome.Cdp;
        var observations = new SiteBrowserGeometryObservations();
        await SiteBrowserAssertions.SelectValue(cdp, SiteBrowserUiTokens.SelectorProfile, profile, cancellationToken);
        await SiteBrowserAssertions.WaitForLoaded(cdp, cancellationToken);
        var path = Path.Combine(inputs.Reports, profile, SiteTokens.ReportFile);
        var report = JsonSerializer.Deserialize(await File.ReadAllBytesAsync(path, cancellationToken),
            SiteReportJsonContext.Create().SiteReport) ?? throw new JsonException();
        foreach (var scenario in SiteTokens.Scenarios)
        {
            await SiteBrowserAssertions.ClickScenario(cdp, scenario, cancellationToken);
            await SiteBrowserAssertions.AssertQueueMetricAvailability(cdp, scenario, cancellationToken);
            foreach (var metric in EnabledMetrics(scenario))
            {
                await SiteBrowserAssertions.SelectValue(cdp, SiteBrowserUiTokens.SelectorMetric, metric, cancellationToken);
                var snapshot = await SiteBrowserAssertions.ReadSnapshot(cdp, cancellationToken);
                var expected = SiteMeasurementOracle.Rows(report, scenario, metric, SiteTokens.MedianRepetition);
                observations.Record(expected, metric);
                await SiteBrowserAssertions.AssertSnapshot(snapshot, expected);
                await SiteBrowserGeometryAssertions.AssertGeometry(snapshot, expected, metric, logarithmic: false);
                var logarithmic = await cdp.EvaluateAsync(SiteBrowserUiTokens.LogScaleScript, false, cancellationToken);
                await Assert.That(logarithmic.GetBoolean()).IsTrue();
                var logSnapshot = await SiteBrowserAssertions.ReadSnapshot(cdp, cancellationToken);
                await SiteBrowserGeometryAssertions.AssertGeometry(logSnapshot, expected, metric, logarithmic: true);
                var linear = await cdp.EvaluateAsync(SiteBrowserUiTokens.LogScaleScript, false, cancellationToken);
                await Assert.That(linear.GetBoolean()).IsFalse();
            }
        }
        if (profile == SiteTokens.SmokeProfile)
        {
            await observations.AssertRepresentativeStates();
        }
        await AssertQueueMetricTransition(cdp, report, cancellationToken);
        await SiteBrowserAssertions.ClickScenario(cdp, SiteTokens.PointRead, cancellationToken);
        await SiteBrowserAssertions.SelectValue(cdp, SiteBrowserUiTokens.SelectorMetric, SiteTokens.ThroughputMetric, cancellationToken);
        for (var repetition = SiteTokens.Zero; repetition < report.Options.Repetitions; repetition++)
        {
            var selected = repetition.ToString(CultureInfo.InvariantCulture);
            await SiteBrowserAssertions.SelectValue(cdp, SiteBrowserUiTokens.SelectorRepetition, selected, cancellationToken);
            var snapshot = await SiteBrowserAssertions.ReadSnapshot(cdp, cancellationToken);
            var expected = SiteMeasurementOracle.Rows(report, SiteTokens.PointRead, SiteTokens.ThroughputMetric, selected);
            await SiteBrowserAssertions.AssertSnapshot(snapshot, expected);
        }
        await SiteBrowserAssertions.SelectValue(cdp, SiteBrowserUiTokens.SelectorRepetition,
            SiteTokens.MedianRepetition, cancellationToken);
    }

    private static async Task AssertQueueMetricTransition(SiteBrowserCdpClient cdp, SiteReport report,
        CancellationToken cancellationToken)
    {
        await SiteBrowserAssertions.ClickScenario(cdp, SiteTokens.QueueCycle, cancellationToken);
        await SiteBrowserAssertions.SelectValue(cdp, SiteBrowserUiTokens.SelectorMetric,
            SiteTokens.AckMetric, cancellationToken);
        await SiteBrowserAssertions.ClickScenario(cdp, SiteTokens.PointRead, cancellationToken);
        await SiteBrowserAssertions.AssertQueueMetricAvailability(cdp, SiteTokens.PointRead, cancellationToken);
        var snapshot = await SiteBrowserAssertions.ReadSnapshot(cdp, cancellationToken);
        var expected = SiteMeasurementOracle.Rows(report, SiteTokens.PointRead, SiteTokens.ThroughputMetric,
            SiteTokens.MedianRepetition);
        await SiteBrowserAssertions.AssertSnapshot(snapshot, expected);
    }

    private static IEnumerable<string> EnabledMetrics(string scenario) => SiteTokens.Metrics.Where(metric =>
        scenario == SiteTokens.QueueCycle || metric is not (SiteTokens.EnqueueMetric or SiteTokens.ReceiveMetric or SiteTokens.AckMetric));
}

internal static class SiteBrowserAssertions
{
    private static readonly CultureInfo DisplayCulture = CultureInfo.GetCultureInfo(SiteBrowserUiTokens.InvariantCultureName);

    public static async Task WaitForLoaded(SiteBrowserCdpClient cdp, CancellationToken cancellationToken)
    {
        var ready = await cdp.WaitForExpressionAsync(SiteBrowserUiTokens.BrowserWaitScript, cancellationToken);
        await Assert.That(ready).IsTrue();
    }

    public static async Task AssertInitialEvidence(SiteBrowserCdpClient cdp, SiteTestInputs inputs,
        CancellationToken cancellationToken)
    {
        var state = await cdp.EvaluateAsync(SiteBrowserUiTokens.InitialEvidenceScript, false, cancellationToken);
        await Assert.That(state.GetProperty(SiteBrowserUiTokens.EngineCountField).GetString()).IsEqualTo(
            SiteTokens.HistoricalTargetCount.ToString(CultureInfo.InvariantCulture));
        await Assert.That(state.GetProperty(SiteBrowserUiTokens.RevisionField).GetString()!.Contains(
            inputs.MeasuredRevision[..SiteBrowserUiTokens.RevisionPrefixLength], StringComparison.Ordinal)).IsTrue();
        await Assert.That(state.GetProperty(SiteBrowserUiTokens.DownloadsField).GetArrayLength()).IsEqualTo(
            SiteBrowserUiTokens.DownloadCount);
        await Assert.That(state.GetProperty(SiteBrowserUiTokens.TableCaptionField).GetString()?.Length > SiteTokens.Zero).IsTrue();
        await Assert.That(state.GetProperty(SiteBrowserUiTokens.HeroDisplayField).GetString()).IsEqualTo(
            SiteBrowserUiTokens.ExpectedGrid);
        await Assert.That(state.GetProperty(SiteBrowserUiTokens.PosterLoadedField).GetBoolean()).IsTrue();
        var toggled = await cdp.EvaluateAsync(SiteBrowserUiTokens.LogScaleScript, false, cancellationToken);
        await Assert.That(toggled.GetBoolean()).IsTrue();
        var toggledBack = await cdp.EvaluateAsync(SiteBrowserUiTokens.LogScaleScript, false, cancellationToken);
        await Assert.That(toggledBack.GetBoolean()).IsFalse();
    }

    public static Task SelectValue(SiteBrowserCdpClient cdp, string selector, string value,
        CancellationToken cancellationToken)
    {
        var expression = ScriptWithArguments(SiteBrowserUiTokens.SelectValueScript,
            JsonSerializer.Serialize(selector), JsonSerializer.Serialize(value));
        return EvaluateSuccess(cdp, expression, cancellationToken);
    }

    public static Task ClickScenario(SiteBrowserCdpClient cdp, string scenario, CancellationToken cancellationToken)
    {
        var expression = ScriptWithArguments(SiteBrowserUiTokens.ClickScenarioScript,
            JsonSerializer.Serialize(SiteBrowserUiTokens.ScenarioTabPrefix + scenario));
        return EvaluateSuccess(cdp, expression, cancellationToken);
    }

    public static async Task AssertQueueMetricAvailability(SiteBrowserCdpClient cdp, string scenario,
        CancellationToken cancellationToken)
    {
        var shouldDisable = scenario != SiteTokens.QueueCycle;
        foreach (var metric in new[] { SiteTokens.EnqueueMetric, SiteTokens.ReceiveMetric, SiteTokens.AckMetric })
        {
            var expression = ScriptWithArguments(SiteBrowserUiTokens.QueueMetricDisabledScript,
                JsonSerializer.Serialize(metric));
            var disabled = await cdp.EvaluateAsync(expression, false, cancellationToken);
            await Assert.That(disabled.GetBoolean()).IsEqualTo(shouldDisable);
        }
    }

    public static async Task<JsonElement> ReadSnapshot(SiteBrowserCdpClient cdp, CancellationToken cancellationToken)
    {
        var snapshot = await cdp.EvaluateAsync(SiteBrowserUiTokens.SnapshotScript, false, cancellationToken);
        await Assert.That(snapshot.GetProperty(SiteBrowserUiTokens.ChartField).GetArrayLength()).IsEqualTo(
            SiteTokens.HistoricalTargetCount);
        return snapshot;
    }

    public static async Task AssertSnapshot(JsonElement actual, IReadOnlyList<OracleRow> expected)
    {
        var chart = actual.GetProperty(SiteBrowserUiTokens.ChartField);
        var table = actual.GetProperty(SiteBrowserUiTokens.TableField);
        var statuses = actual.GetProperty(SiteBrowserUiTokens.StatusesField);
        await Assert.That(chart.GetArrayLength()).IsEqualTo(expected.Count);
        await Assert.That(table.GetArrayLength()).IsEqualTo(expected.Count);
        await Assert.That(statuses.GetArrayLength()).IsEqualTo(expected.Count);
        for (var index = SiteTokens.Zero; index < expected.Count; index++)
        {
            var row = expected[index];
            var chartRow = chart[index];
            await Assert.That(chartRow.GetProperty(SiteBrowserUiTokens.NameField).GetString()).IsEqualTo(row.Name);
            var tableRow = table[index];
            await Assert.That(tableRow[SiteTokens.Zero].GetString()).IsEqualTo(row.Name);
            await Assert.That(tableRow[SiteTokens.One].GetString()).IsEqualTo(Display(row.Throughput));
            await Assert.That(tableRow[SiteBrowserUiTokens.Two].GetString()).IsEqualTo(Display(row.P50));
            await Assert.That(tableRow[SiteBrowserUiTokens.Three].GetString()).IsEqualTo(Display(row.P95));
            await Assert.That(tableRow[SiteBrowserUiTokens.Four].GetString()).IsEqualTo(Display(row.P99));
            var completion = row.Attempts == SiteTokens.Zero ? SiteBrowserUiTokens.MissingValue :
                Display(row.Successes) + SiteBrowserUiTokens.AttemptsSeparator + Display(row.Attempts);
            await Assert.That(tableRow[SiteBrowserUiTokens.Five].GetString()).IsEqualTo(completion);
            await Assert.That(statuses[index].GetString()).IsEqualTo(row.Status);
            var chartLabel = row.Value is not null ? Display(row.Value) :
                row.Status == SiteTokens.FailedStatus ? SiteBrowserUiTokens.FailedLabel : SiteBrowserUiTokens.UnsupportedLabel;
            var chartStatus = row.Value is not null ? SiteTokens.MeasuredStatus :
                row.Status == SiteTokens.FailedStatus ? SiteTokens.FailedStatus : SiteTokens.UnsupportedStatus;
            await Assert.That(chartRow.GetProperty(SiteBrowserTokens.ValueField).GetString()).IsEqualTo(chartLabel);
            await Assert.That(chartRow.GetProperty(SiteBrowserUiTokens.StatusField).GetString()).IsEqualTo(chartStatus);
        }
    }

    public static async Task AssertKeyboardAndDownloads(SiteBrowserSession browser, SiteTestInputs inputs,
        CancellationToken cancellationToken)
    {
        var cdp = browser.Chrome.Cdp;
        await cdp.EvaluateAsync(SiteBrowserUiTokens.FocusFirstScenarioScript, false, cancellationToken);
        await cdp.CommandAsync(SiteBrowserTokens.InputKeyDown, new Dictionary<string, object?>
        {
            [SiteBrowserTokens.TypeField] = SiteBrowserUiTokens.KeyboardKeyDown,
            [SiteBrowserTokens.KeyField] = SiteBrowserUiTokens.ArrowRight,
            [SiteBrowserTokens.CodeField] = SiteBrowserUiTokens.ArrowRight,
        }, cancellationToken);
        await cdp.CommandAsync(SiteBrowserTokens.InputKeyDown, new Dictionary<string, object?>
        {
            [SiteBrowserTokens.TypeField] = SiteBrowserUiTokens.KeyboardKeyUp,
            [SiteBrowserTokens.KeyField] = SiteBrowserUiTokens.ArrowRight,
            [SiteBrowserTokens.CodeField] = SiteBrowserUiTokens.ArrowRight,
        }, cancellationToken);
        var focused = await cdp.EvaluateAsync(SiteBrowserUiTokens.FocusedScenarioScript, false, cancellationToken);
        await Assert.That(focused.GetString()).IsEqualTo(SiteBrowserUiTokens.ScenarioTabPrefix + SiteTokens.DocumentWrite);

        foreach (var file in SiteBrowserUiTokens.DownloadFiles)
        {
            await DownloadAndCompare(cdp, inputs, browser.Output, file, cancellationToken);
        }
    }

    private static async Task DownloadAndCompare(SiteBrowserCdpClient cdp, SiteTestInputs inputs, string output,
        string file, CancellationToken cancellationToken)
    {
        var linkId = SiteBrowserUiTokens.DownloadId(file);
        var expression = ScriptWithArguments(SiteBrowserUiTokens.ClickByIdScript,
            JsonSerializer.Serialize(linkId));
        await EvaluateSuccess(cdp, expression, cancellationToken);
        var destination = Path.Combine(Path.GetDirectoryName(output)!, SiteBrowserTokens.DownloadDirectory, file);
        for (var attempt = SiteTokens.Zero; attempt < SiteBrowserUiTokens.DownloadWaitAttempts; attempt++)
        {
            if (File.Exists(destination))
            {
                break;
            }
            await Task.Delay(SiteBrowserUiTokens.DownloadPollMilliseconds, cancellationToken);
        }
        var expected = Path.Combine(inputs.Reports, SiteTokens.SmokeProfile, file);
        await Assert.That(File.Exists(destination)).IsTrue();
        var actualBytes = await File.ReadAllBytesAsync(destination, cancellationToken);
        var expectedBytes = await File.ReadAllBytesAsync(expected, cancellationToken);
        await Assert.That(actualBytes.SequenceEqual(expectedBytes)).IsTrue();
    }

    internal static async Task EvaluateSuccess(SiteBrowserCdpClient cdp, string expression, CancellationToken cancellationToken)
    {
        var result = await cdp.EvaluateAsync(expression, false, cancellationToken);
        await Assert.That(result.GetBoolean()).IsTrue();
    }

    internal static string Display(double? value)
    {
        if (value is null)
        {
            return SiteBrowserUiTokens.MissingValue;
        }
        var shortestDecimal = decimal.Parse(value.Value.ToString(SiteBrowserUiTokens.RoundTripNumberPattern, CultureInfo.InvariantCulture),
            NumberStyles.Float, CultureInfo.InvariantCulture);
        var rounded = decimal.Round(shortestDecimal, SiteBrowserUiTokens.DisplayDecimalPlaces, MidpointRounding.AwayFromZero);
        return rounded.ToString(SiteBrowserUiTokens.FormatNumberPattern, DisplayCulture);
    }

    private static string ScriptWithArguments(string template, string first, string? second = null)
    {
        var expression = template.Replace(SiteBrowserUiTokens.FirstScriptArgument, first, StringComparison.Ordinal);
        return second is null ? expression :
            expression.Replace(SiteBrowserUiTokens.SecondScriptArgument, second, StringComparison.Ordinal);
    }
}
