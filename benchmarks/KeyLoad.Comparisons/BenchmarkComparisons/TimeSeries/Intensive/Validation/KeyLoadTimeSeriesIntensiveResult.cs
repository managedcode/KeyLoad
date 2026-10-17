using System.Collections.Immutable;
using System.Runtime.InteropServices;
using ManagedCode.Communication;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class KeyLoadTimeSeriesIntensiveResult
{
    internal static T Value<T>(Result<T> result)
    {
        if (!result.IsSuccess)
        {
            throw Problem(result.Problem);
        }

        return result.Value is null
            ? throw new KeyLoadTimeSeriesIntensiveReplyException((long?)null)
            : result.Value!;
    }

    internal static ImmutableArray<SampleRecord> Raw(Result<SampleRecord[]> result)
    {
        var records = Value(result);
        return ImmutableCollectionsMarshal.AsImmutableArray(records);
    }

    internal static SampleRecord? Latest(Result<LatestSampleResult> result) => Value(result).Sample;

    internal static ImmutableArray<SampleAggregateWindow> Windows(Result<SampleAggregateWindowsResult> result)
    {
        var windows = Value(result).Windows;
        return windows.IsDefault
            ? throw new KeyLoadTimeSeriesIntensiveReplyException((long?)null)
            : windows;
    }

    private static KeyLoadTimeSeriesIntensiveProblemException Problem(Problem? problem)
    {
        var code = ParseCode(problem?.ErrorCode);
        int? status = problem is { StatusCode: >= KeyLoadTimeSeriesIntensiveProtocol.MinimumPositiveHttpStatus }
            ? problem.StatusCode : null;
        return new(code, status);
    }

    private static ErrorCode? ParseCode(string? raw)
    {
        if (raw is null || !Enum.TryParse<ErrorCode>(raw, ignoreCase: false, out var code)
            || !Enum.IsDefined(code) || !string.Equals(code.ToString(), raw, StringComparison.Ordinal))
        {
            return null;
        }

        return code;
    }
}
