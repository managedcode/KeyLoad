using System.Globalization;
using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteBrowserGeometryAssertions
{
    private static readonly CultureInfo DisplayCulture = CultureInfo.GetCultureInfo(SiteBrowserGeometryTokens.DisplayCultureName);

    public static async Task AssertGeometry(JsonElement snapshot, IReadOnlyList<OracleRow> expected, string metric,
        bool logarithmic)
    {
        var chart = snapshot.GetProperty(SiteBrowserUiTokens.ChartField);
        var maximum = Math.Max(SiteBrowserGeometryTokens.MinimumAxisMaximum,
            expected.Where(row => row.Value is not null).Select(row => row.Maximum ?? row.Value!.Value)
                .DefaultIfEmpty(SiteBrowserGeometryTokens.MinimumAxisMaximum).Max());
        await Assert.That(snapshot.GetProperty(SiteBrowserGeometryTokens.DirectionField).GetString()).IsEqualTo(Direction(metric));
        for (var index = SiteTokens.Zero; index < expected.Count; index++)
        {
            var row = expected[index];
            var actual = chart[index];
            await Assert.That(actual.GetProperty(SiteBrowserGeometryTokens.HasFillField).GetBoolean()).IsEqualTo(row.Value is not null);
            var valueWidth = row.Value is null ? SiteBrowserGeometryTokens.Zero : Scale(row.Value.Value, maximum, logarithmic);
            await Assert.That(actual.GetProperty(SiteBrowserGeometryTokens.WidthField).GetDouble())
                .IsEqualTo(valueWidth).Within(SiteBrowserGeometryTokens.Tolerance);
            var hasRange = row.Value is not null && !string.Equals(metric, SiteTokens.ErrorMetric, StringComparison.Ordinal)
                && row.Minimum != row.Maximum;
            await Assert.That(actual.GetProperty(SiteBrowserGeometryTokens.HasRangeField).GetBoolean()).IsEqualTo(hasRange);
            var minimum = hasRange ? Scale(row.Minimum!.Value, maximum, logarithmic) : SiteBrowserGeometryTokens.Zero;
            var range = hasRange ? Scale(row.Maximum!.Value, maximum, logarithmic) - minimum : SiteBrowserGeometryTokens.Zero;
            await Assert.That(actual.GetProperty(SiteBrowserGeometryTokens.MinimumField).GetDouble())
                .IsEqualTo(minimum).Within(SiteBrowserGeometryTokens.Tolerance);
            await Assert.That(actual.GetProperty(SiteBrowserGeometryTokens.RangeField).GetDouble())
                .IsEqualTo(range).Within(SiteBrowserGeometryTokens.Tolerance);
        }

        await AssertAxis(snapshot.GetProperty(SiteBrowserGeometryTokens.AxisField), maximum, logarithmic);
    }

    private static async Task AssertAxis(JsonElement axis, double maximum, bool logarithmic)
    {
        await Assert.That(axis.GetArrayLength()).IsEqualTo(SiteBrowserGeometryTokens.AxisTickCount + SiteTokens.One);
        var precision = maximum < SiteBrowserGeometryTokens.AxisPrecisionThreshold
            ? SiteBrowserGeometryTokens.FractionalAxisPrecision : SiteBrowserGeometryTokens.IntegerAxisPrecision;
        for (var tick = SiteTokens.Zero; tick <= SiteBrowserGeometryTokens.AxisTickCount; tick++)
        {
            var fraction = tick / (double)SiteBrowserGeometryTokens.AxisTickCount;
            var value = logarithmic
                ? Math.Pow(SiteTokens.One + maximum, fraction) - SiteTokens.One
                : maximum * fraction;
            var actual = double.Parse(axis[tick].GetString()!, NumberStyles.Number, DisplayCulture);
            var expected = Math.Round(value, precision, MidpointRounding.AwayFromZero);
            await Assert.That(actual).IsEqualTo(expected).Within(SiteBrowserGeometryTokens.AxisRoundingTolerance);
        }
    }

    private static double Scale(double value, double maximum, bool logarithmic) => logarithmic
        ? Math.Log10(SiteTokens.One + value) / Math.Log10(SiteTokens.One + maximum) * SiteBrowserGeometryTokens.Percent
        : value / maximum * SiteBrowserGeometryTokens.Percent;

    private static string Direction(string metric) => metric switch
    {
        SiteTokens.ThroughputMetric => SiteBrowserGeometryTokens.HigherDirection,
        SiteTokens.ErrorMetric => SiteBrowserGeometryTokens.BetterDirection,
        SiteTokens.CpuMetric or SiteTokens.AllocationMetric => SiteBrowserGeometryTokens.GeneratorDirection,
        SiteTokens.RssMetric => SiteBrowserGeometryTokens.ResourceDirection,
        _ => SiteBrowserGeometryTokens.LowerDirection,
    };
}

internal static class SiteBrowserGeometryTokens
{
    public const string DisplayCultureName = "en-US";
    public const string DirectionField = "direction";
    public const string AxisField = "axis";
    public const string WidthField = "width";
    public const string MinimumField = "minimum";
    public const string RangeField = "range";
    public const string HasFillField = "hasFill";
    public const string HasRangeField = "hasRange";
    public const string HigherDirection = "Higher is faster";
    public const string LowerDirection = "Lower is faster";
    public const string BetterDirection = "Lower is better";
    public const string GeneratorDirection = "Lower generator usage";
    public const string ResourceDirection = "Generator process usage";
    public const double MinimumAxisMaximum = 1d;
    public const double Percent = 100d;
    public const double Tolerance = 0.0000001d;
    public const double AxisRoundingTolerance = 0.0000001d;
    public const double AxisPrecisionThreshold = 10d;
    public const int AxisTickCount = 4;
    public const int FractionalAxisPrecision = 2;
    public const int IntegerAxisPrecision = 0;
    public const int Zero = 0;
}

internal sealed class SiteBrowserGeometryObservations
{
    private bool observedNull;
    private bool observedZero;
    private bool observedWhisker;

    public void Record(IReadOnlyList<OracleRow> rows, string metric)
    {
        observedNull |= rows.Any(row => row.Value is null);
        observedZero |= rows.Any(row => row.Value == SiteBrowserGeometryTokens.Zero);
        observedWhisker |= metric != SiteTokens.ErrorMetric && rows.Any(row => row.Value is not null && row.Minimum != row.Maximum);
    }

    public async Task AssertRepresentativeStates()
    {
        await Assert.That(observedNull).IsTrue();
        await Assert.That(observedZero).IsTrue();
        await Assert.That(observedWhisker).IsTrue();
    }
}
