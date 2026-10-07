using ManagedCode.TimeSeries.Abstractions;
using ManagedCode.TimeSeries.Summers;

namespace KeyLoad.Core.Features.TimeSeries;

internal sealed class SampleAggregateAccumulator
{
    private const int EmptySampleCount = 0;
    private const int EmptySum = 0;

    private const int OneNativeBucket = 1;
    private const string NonFiniteAggregate = "The time-series aggregate is not finite.";
    private readonly DoubleTimeSeriesSummer sum = new(TimeSpan.MaxValue, OneNativeBucket, Strategy.Sum);
    private readonly DoubleTimeSeriesSummer minimum = new(TimeSpan.MaxValue, OneNativeBucket, Strategy.Min);
    private readonly DoubleTimeSeriesSummer maximum = new(TimeSpan.MaxValue, OneNativeBucket, Strategy.Max);
    private double finiteGuardSum;

    internal static SampleAggregate Empty => new(EmptySampleCount, EmptySum, null, null, null);

    internal void Add(SampleRecord sample)
    {
        var timestamp = sample.Sample.Timestamp;
        var value = sample.Sample.Value;
        finiteGuardSum += value;
        if (!double.IsFinite(finiteGuardSum))
        {
            throw Errors.Fail(ErrorCode.Validation, NonFiniteAggregate);
        }

        sum.AddNewData(timestamp, value);
        minimum.AddNewData(timestamp, value);
        maximum.AddNewData(timestamp, value);
    }

    internal SampleAggregate Complete()
    {
        const int EmptyCount = 0;

        var count = checked((long)sum.DataCount);
        if (count == EmptyCount)
        {
            return Empty;
        }

        var total = sum.Sum();
        if (!double.IsFinite(total))
        {
            throw Errors.Fail(ErrorCode.Validation, NonFiniteAggregate);
        }

        return Reconstruct(count, total, minimum.Min(), maximum.Max());
    }

    internal static SampleAggregate Reconstruct(long count, double total, double? minimum, double? maximum)
    {
        if (count == EmptySampleCount)
        { return Empty; }
        var average = total / count;
        if (!double.IsFinite(average))
        {
            throw Errors.Fail(ErrorCode.Validation, NonFiniteAggregate);
        }

        return new(count, total, minimum, maximum, average);
    }
}
