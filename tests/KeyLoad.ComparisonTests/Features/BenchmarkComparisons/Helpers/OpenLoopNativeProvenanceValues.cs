using System.Globalization;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class OpenLoopNativeProvenanceValues
{
    internal static bool IsPresent(string? value)
        => value is { Length: > OpenLoopNativeTestOracle.FirstIndex };

    internal static bool TryPositiveInt64(string? value, out long parsed)
        => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed)
            && parsed >= OpenLoopNativeTestOracle.MinimumPositiveIdentity;

    internal static bool TryPositiveInt32(string? value, out int parsed)
        => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed)
            && parsed >= OpenLoopNativeTestOracle.MinimumPositiveIdentity;
}
