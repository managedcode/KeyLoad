namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveResponseReader
{
    internal static async Task<TimeSeriesIntensiveResponse?> ReadAsync(ITimeSeriesIntensiveTarget target,
        TimeSeriesIntensiveExpectations expected, TimeSeriesIntensivePreparedPhase phase, int index,
        TimeSeriesIntensiveCallScope call, TimeSeriesIntensiveConcurrency concurrency)
    {
        if (!call.AcceptInvocation())
        {
            return null;
        }

        var range = expected.Ranges[index % TimeSeriesIntensiveProfile.RangeGroups];
        concurrency.EnterRequest();
        call.Start();
        try
        {
            var response = await InvokeAsync(target, expected, phase, index, range, call.Token).ConfigureAwait(false);
            call.Stop();
            return response;
        }
        catch (Exception)
        {
            call.Stop();
            throw;
        }
        finally
        {
            concurrency.ExitRequest();
        }
    }

    private static async Task<TimeSeriesIntensiveResponse> InvokeAsync(ITimeSeriesIntensiveTarget target,
        TimeSeriesIntensiveExpectations expected, TimeSeriesIntensivePreparedPhase phase, int index,
        TimeSeriesIntensiveReadPlan range, CancellationToken cancellationToken) => phase.Scenario switch
        {
            TimeSeriesIntensiveScenario.Append => new(Receipt: await target.AppendAsync(phase.SeriesId, phase.Commands[index],
                expected.AppendSamples[index], TimeSeriesIntensiveProfile.Tags, cancellationToken).ConfigureAwait(false)),
            TimeSeriesIntensiveScenario.RawRangeRead => new(Raw: await target.ReadAsync(phase.SeriesId, range.From, range.Until,
                TimeSeriesIntensiveProfile.RawLimit, cancellationToken).ConfigureAwait(false)),
            TimeSeriesIntensiveScenario.Latest => new(Latest: await target.LatestAsync(phase.SeriesId,
                expected.LatestCuts[index % TimeSeriesIntensiveProfile.GroupCount], cancellationToken).ConfigureAwait(false)),
            TimeSeriesIntensiveScenario.Aggregate => new(Aggregate: await target.AggregateAsync(phase.SeriesId, range.From, range.Until,
                TimeSeriesIntensiveProfile.MaxSamples, cancellationToken).ConfigureAwait(false)),
            TimeSeriesIntensiveScenario.Windows => new(Windows: await target.WindowsAsync(phase.SeriesId, range.From, range.Until,
                TimeSeriesIntensiveProfile.WindowWidth, TimeSeriesIntensiveProfile.MaxSamples,
                TimeSeriesIntensiveProfile.MaxWindows, cancellationToken).ConfigureAwait(false)),
            _ => throw new ArgumentOutOfRangeException(nameof(phase))
        };
}
