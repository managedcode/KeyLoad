using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveSetupExecutor
{
    internal static async Task InitializeAsync(ITimeSeriesIntensiveTarget target, IOptions<NativeComparisonExecutionOptions> executionOptions, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        using var call = new TimeSeriesIntensiveCallScope(executionOptions, cancellationToken, provider: timeProvider);
        call.Start();
        try
        {
            await target.InitializeAsync(call.Token).ConfigureAwait(false);
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
    }

    internal static async Task SeedAsync(ITimeSeriesIntensiveTarget target, IOptions<NativeComparisonExecutionOptions> executionOptions, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        const int NoObservedItems = 0;

        for (var batch = NoObservedItems; batch < TimeSeriesIntensiveProfile.SeedBatchCount; batch++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var samples = TimeSeriesIntensiveCorpus.SeedBatch(batch);
            using var call = new TimeSeriesIntensiveCallScope(executionOptions, cancellationToken, provider: timeProvider);
            call.Start();
            try
            {
                await target.SeedAsync(TimeSeriesIntensiveProfile.SeedSeries, samples, TimeSeriesIntensiveProfile.Tags, call.Token).ConfigureAwait(false);
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
        }
    }
}
