using System.Reflection;
using ManagedCode.TimeSeries.Summers;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;

internal static class TimeSeriesComparisonPackageVersion
{
    private const char BuildMetadataSeparator = '+';
    private const string MissingVersion = "The actual TimeSeries assembly has no package version metadata.";

    internal static string Read()
    {
        var informational = typeof(DoubleTimeSeriesSummer).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
        {
            throw new InvalidOperationException(MissingVersion);
        }

        var separator = informational.IndexOf(BuildMetadataSeparator, StringComparison.Ordinal);
        if (separator == 0)
        {
            throw new InvalidOperationException(MissingVersion);
        }

        return separator < 0 ? informational : informational[..separator];
    }
}
