using System.Collections.Immutable;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveVerificationReader
{
    internal static async Task<ImmutableArray<SampleRecord>> ReadAsync(ITimeSeriesIntensiveTarget target, string seriesId, DateTimeOffset from, DateTimeOffset until, IOptions<NativeComparisonExecutionOptions> executionOptions, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        using var call = new TimeSeriesIntensiveCallScope(executionOptions, cancellationToken, provider: timeProvider);
        call.Start();
        ImmutableArray<SampleRecord> actual;
        try
        {
            actual = await target.ReadAsync(seriesId, from, until, TimeSeriesIntensiveProfile.RawLimit, call.Token).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            var observed = call.ObserveFailure(error);
            if (ReferenceEquals(error, observed))
            {
                throw;
            }

            throw observed;
        }

        call.Stop();
        call.RequireSuccess();
        return actual;
    }

    internal static async Task<SampleRecord?> LatestAsync(ITimeSeriesIntensiveTarget target, string seriesId, IOptions<NativeComparisonExecutionOptions> executionOptions, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        using var call = new TimeSeriesIntensiveCallScope(executionOptions, cancellationToken, provider: timeProvider);
        call.Start();
        SampleRecord? actual;
        try
        {
            actual = await target.LatestAsync(seriesId, null, call.Token).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            var observed = call.ObserveFailure(error);
            if (ReferenceEquals(error, observed))
            {
                throw;
            }

            throw observed;
        }

        call.Stop();
        call.RequireSuccess();
        return actual;
    }

    internal static async Task<SampleAggregate> WholeAsync(ITimeSeriesIntensiveTarget target, string seriesId, IOptions<NativeComparisonExecutionOptions> executionOptions, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        using var call = new TimeSeriesIntensiveCallScope(executionOptions, cancellationToken, provider: timeProvider);
        call.Start();
        SampleAggregate actual;
        try
        {
            actual = await target.AggregateAsync(seriesId, DateTimeOffset.MinValue, null, TimeSeriesIntensiveProfile.MaxSamples, call.Token).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            var observed = call.ObserveFailure(error);
            if (ReferenceEquals(error, observed))
            {
                throw;
            }

            throw observed;
        }

        call.Stop();
        call.RequireSuccess();
        return actual;
    }
}
