using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensivePlans
{
    internal static TimeSeriesIntensiveReadPlan Read(int index)
    {
        TimeSeriesIntensiveCorpus.ValidateIndex(index);
        var from = TimeSeriesIntensiveProfile.Epoch.AddMinutes(index % TimeSeriesIntensiveProfile.RangeGroups * TimeSeriesIntensiveProfile.GroupMinutes + TimeSeriesIntensiveProfile.RangeFromMinute);
        var latest = TimeSeriesIntensiveProfile.Epoch.AddMinutes(index % TimeSeriesIntensiveProfile.GroupCount * TimeSeriesIntensiveProfile.GroupMinutes + TimeSeriesIntensiveProfile.LatestMinute);
        return new(from, from.AddMinutes(TimeSeriesIntensiveProfile.RangeMinutes), latest);
    }

    internal static string PhaseSeries(int repetition, bool warmup)
    {
        ValidateRepetition(repetition);
        return (warmup ? TimeSeriesIntensiveProfile.WarmSeriesPrefix : TimeSeriesIntensiveProfile.MeasuredSeriesPrefix) + repetition.ToString(CultureInfo.InvariantCulture);
    }

    internal static string CommandPurpose(int repetition, bool warmup, int index)
    {
        ValidateRepetition(repetition);
        TimeSeriesIntensiveCorpus.ValidateIndex(index);
        if (warmup)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, TimeSeriesIntensiveProfile.WarmupCount);
        }

        return (warmup ? TimeSeriesIntensiveProfile.WarmupPhase : TimeSeriesIntensiveProfile.MeasuredPhase) + TimeSeriesIntensiveProfile.CommandSeparator + repetition.ToString(CultureInfo.InvariantCulture)
            + TimeSeriesIntensiveProfile.CommandSeparator + index.ToString(CultureInfo.InvariantCulture);
    }

    internal static Guid CommandId(string runId, string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(runId + TimeSeriesIntensiveProfile.CommandSeparator + purpose));
        return new(hash.AsSpan(0, TimeSeriesIntensiveProfile.CommandIdBytes));
    }

    internal static IEnumerable<TimeSeriesIntensiveReadback> SeedReadbacks()
    {
        for (var chunk = 0; chunk < TimeSeriesIntensiveProfile.SeedBatchCount; chunk++)
        {
            yield return new(TimeSeriesIntensiveProfile.SeedSeries,
                TimeSeriesIntensiveProfile.Epoch.AddMinutes(chunk * TimeSeriesIntensiveProfile.SeedReadbackMinutes),
                TimeSeriesIntensiveProfile.Epoch.AddMinutes(chunk * TimeSeriesIntensiveProfile.SeedReadbackMinutes + TimeSeriesIntensiveProfile.SeedReadbackUntilMinute), TimeSeriesIntensiveProfile.SeedBatchSize);
        }
    }

    internal static IEnumerable<TimeSeriesIntensiveReadback> AppendReadbacks(int repetition, bool warmup)
    {
        var series = PhaseSeries(repetition, warmup);
        var count = warmup ? TimeSeriesIntensiveProfile.WarmupCount : TimeSeriesIntensiveProfile.OperationCount;
        for (var first = 0; first < count; first += TimeSeriesIntensiveProfile.RawLimit)
        {
            var size = Math.Min(TimeSeriesIntensiveProfile.RawLimit, count - first);
            yield return new(series, TimeSeriesIntensiveProfile.Epoch.AddDays(TimeSeriesIntensiveProfile.AppendEpochDays).AddMilliseconds(first),
                TimeSeriesIntensiveProfile.Epoch.AddDays(TimeSeriesIntensiveProfile.AppendEpochDays).AddMilliseconds(first + size - 1), size);
        }
    }

    internal static int WholeSeriesCount(string seriesId)
    {
        if (string.Equals(seriesId, TimeSeriesIntensiveProfile.SeedSeries, StringComparison.Ordinal))
        {
            return TimeSeriesIntensiveProfile.SampleCount;
        }

        for (var repetition = 0; repetition < TimeSeriesIntensiveProfile.RepetitionCount; repetition++)
        {
            if (string.Equals(seriesId, PhaseSeries(repetition, true), StringComparison.Ordinal))
            {
                return TimeSeriesIntensiveProfile.WarmupCount;
            }

            if (string.Equals(seriesId, PhaseSeries(repetition, false), StringComparison.Ordinal))
            {
                return TimeSeriesIntensiveProfile.OperationCount;
            }
        }

        throw new ArgumentException(TimeSeriesIntensiveErrors.UnknownSeries, nameof(seriesId));
    }

    private static void ValidateRepetition(int repetition)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(repetition);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(repetition, TimeSeriesIntensiveProfile.RepetitionCount);
    }
}
