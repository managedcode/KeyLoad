using System.Reflection;
using ManagedCode.TimeSeries.Summers;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;

internal static class TimeSeriesComparisonPackageVersion
{
    private const char BuildMetadataSeparator = '+';
    private const string MissingVersion = "The actual TimeSeries assembly has no package version metadata.";

    internal static string Read()
    {
        const int NoObservedItems = 0;

        var informational = typeof(DoubleTimeSeriesSummer).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
        {
            throw new InvalidOperationException(MissingVersion);
        }

        var separator = informational.IndexOf(BuildMetadataSeparator, StringComparison.Ordinal);
        if (separator == NoObservedItems)
        {
            throw new InvalidOperationException(MissingVersion);
        }

        return separator < NoObservedItems ? informational : informational[..separator];
    }
}
