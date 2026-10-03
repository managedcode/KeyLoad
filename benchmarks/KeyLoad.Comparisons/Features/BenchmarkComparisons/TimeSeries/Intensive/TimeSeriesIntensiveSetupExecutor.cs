namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveSetupExecutor
{
    internal static async Task InitializeAsync(ITimeSeriesIntensiveTarget target, CancellationToken cancellationToken)
    {
        using var call = new TimeSeriesIntensiveCallScope(cancellationToken);
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

    internal static async Task SeedAsync(ITimeSeriesIntensiveTarget target, CancellationToken cancellationToken)
    {
        for (var batch = 0; batch < TimeSeriesIntensiveProfile.SeedBatchCount; batch++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var samples = TimeSeriesIntensiveCorpus.SeedBatch(batch);
            using var call = new TimeSeriesIntensiveCallScope(cancellationToken);
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
