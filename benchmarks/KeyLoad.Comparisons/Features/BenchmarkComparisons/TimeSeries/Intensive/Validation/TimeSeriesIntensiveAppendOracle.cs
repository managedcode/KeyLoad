using System.Collections.Immutable;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveAppendOracle
{
    internal static void ValidateReceipts(string runId, int repetition, bool warmup, IReadOnlyList<TimeSeriesIntensiveAppendReceipt> receipts)
    {
        ArgumentNullException.ThrowIfNull(receipts);
        var series = TimeSeriesIntensivePlans.PhaseSeries(repetition, warmup);
        var count = TimeSeriesIntensivePlans.WholeSeriesCount(series);
        if (receipts.Count != count)
        {
            throw new ComparisonFailureException(TimeSeriesIntensiveErrors.ReceiptCardinality);
        }

        var sequences = new bool[count];
        for (var index = 0; index < count; index++)
        {
            var receipt = receipts[index];
            var command = TimeSeriesIntensivePlans.CommandId(runId, TimeSeriesIntensivePlans.CommandPurpose(repetition, warmup, index));
            if (receipt is null || receipt.CommandId != command || receipt.Sequence < 1 || receipt.Sequence > count)
            {
                throw new ComparisonFailureException(TimeSeriesIntensiveErrors.ReceiptIdentity);
            }

            var slot = (int)receipt.Sequence - 1;
            if (sequences[slot])
            {
                throw new ComparisonFailureException(TimeSeriesIntensiveErrors.DuplicateReceipt);
            }

            sequences[slot] = true;
        }
    }

    internal static ImmutableArray<SampleRecord> Readback(TimeSeriesIntensiveReadback readback, IReadOnlyList<TimeSeriesIntensiveAppendReceipt> receipts)
    {
        ArgumentNullException.ThrowIfNull(readback);
        ArgumentNullException.ThrowIfNull(receipts);
        if (readback.ExpectedCount < 1 || readback.ExpectedCount > TimeSeriesIntensiveProfile.RawLimit
            || string.Equals(readback.SeriesId, TimeSeriesIntensiveProfile.SeedSeries, StringComparison.Ordinal))
        {
            throw new ArgumentException(TimeSeriesIntensiveErrors.InvalidAppendReadback, nameof(readback));
        }

        var offset = readback.From.UtcTicks - TimeSeriesIntensiveProfile.Epoch.AddDays(TimeSeriesIntensiveProfile.AppendEpochDays).UtcTicks;
        var first = offset / TimeSpan.TicksPerMillisecond;
        var until = readback.From.AddMilliseconds(readback.ExpectedCount - 1);
        if (receipts.Count != TimeSeriesIntensivePlans.WholeSeriesCount(readback.SeriesId) || first < 0
            || offset % TimeSpan.TicksPerMillisecond != 0 || until != readback.Until
            || first + readback.ExpectedCount > receipts.Count)
        {
            throw new ArgumentException(TimeSeriesIntensiveErrors.InvalidAppendReadback, nameof(readback));
        }

        var builder = ImmutableArray.CreateBuilder<SampleRecord>(readback.ExpectedCount);
        for (var index = (int)first; index < first + readback.ExpectedCount; index++)
        {
            var receipt = receipts[index];
            if (receipt is null || receipt.Sequence < 1 || receipt.Sequence > receipts.Count)
            {
                throw new ComparisonFailureException(TimeSeriesIntensiveErrors.InvalidReadbackReceipt);
            }

            builder.Add(new(readback.SeriesId, TimeSeriesIntensiveCorpus.AppendSample(index), receipt.Sequence, TimeSeriesIntensiveProfile.Tags));
        }

        return builder.MoveToImmutable();
    }
}
