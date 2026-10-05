using System.Globalization;

namespace KeyLoad.Comparisons;

internal static class OpenLoopRateSelection
{
    internal static bool IsSupported(int rate) => rate is 250 or 1000 or 4000;

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
