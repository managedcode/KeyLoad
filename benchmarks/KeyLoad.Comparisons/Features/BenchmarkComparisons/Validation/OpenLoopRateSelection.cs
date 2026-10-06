using System.Globalization;

namespace KeyLoad.Comparisons;

internal static class OpenLoopRateSelection
{
    private const int HighHistoricalArrivalRate = 4000;

    private const int LowHistoricalArrivalRate = 250;
    private const int MediumHistoricalArrivalRate = 1000;

    internal static bool IsSupported(int rate) => rate is LowHistoricalArrivalRate or MediumHistoricalArrivalRate or HighHistoricalArrivalRate;

    internal static int? Read(string? value)
    {
        if (value is null)
        {
            return null;
        }
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var rate)
            || !IsSupported(rate) || value != rate.ToString(CultureInfo.InvariantCulture))
        {
            throw new InvalidOperationException(ComparisonWorkerSelection.InvalidSelection);
        }
        return rate;
    }
}
