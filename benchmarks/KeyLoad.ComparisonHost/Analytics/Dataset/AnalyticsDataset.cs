using System.Globalization;

namespace KeyLoad.ComparisonHost.Analytics;

/// <summary>One fact: three dimensions and two metrics at one second.</summary>
internal sealed record AnalyticsEvent(string Id, DateTimeOffset At, string Region, string Product, string Channel,
    double Amount, long Quantity);

/// <summary>An aggregated group: its key, event count and the sum of <c>amount</c>.</summary>
internal sealed record GroupTotal(string Key, long Count, double Sum);

/// <summary>
/// The standard analytics dataset: one event per second from 2026-01-01T00:00:00Z across 10 regions,
/// 100 products and 5 channels; <c>amount</c> has one decimal.
/// </summary>
internal static class AnalyticsDataset
{
    internal const int Regions = 10;
    internal const int Products = 100;
    internal const int Channels = 5;
    internal const int WindowEvents = 10_000;
    internal static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private const string IdPrefix = "a";
    private const string IdFormat = "D9";
    private const string RegionPrefix = "r";
    private const string ProductPrefix = "p";
    private const string ProductFormat = "D2";
    private const string ChannelPrefix = "c";

    internal static AnalyticsEvent Create(int number) => new(IdPrefix + number.ToString(IdFormat, CultureInfo.InvariantCulture),
        At(number), Region(number % Regions), Product(number % Products),
        ChannelPrefix + (number % Channels).ToString(CultureInfo.InvariantCulture), Amount(number), number % 7 + 1);

    internal static DateTimeOffset At(int number) => Epoch.AddSeconds(number);

    internal static string Region(int region) => RegionPrefix + region.ToString(CultureInfo.InvariantCulture);

    internal static string Product(int product) => ProductPrefix + product.ToString(ProductFormat, CultureInfo.InvariantCulture);

    internal static double Amount(int number) => (number % 1000) / 10d;

    internal static bool Close(double actual, double expected) => Math.Abs(actual - expected) <= 1e-6 * Math.Max(1, Math.Abs(expected));
}
