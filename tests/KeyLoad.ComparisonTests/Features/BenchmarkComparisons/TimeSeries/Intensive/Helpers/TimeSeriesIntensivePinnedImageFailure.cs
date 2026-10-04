using System.ComponentModel;
using System.Text.Json;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensivePinnedImageFailure
{
    internal static bool IsNative(Exception error) => error is OperationCanceledException or IOException
        or InvalidOperationException or Win32Exception or UnauthorizedAccessException or ArgumentException
        or JsonException or KeyNotFoundException or FormatException or OverflowException or NotSupportedException or TimeoutException;
}
