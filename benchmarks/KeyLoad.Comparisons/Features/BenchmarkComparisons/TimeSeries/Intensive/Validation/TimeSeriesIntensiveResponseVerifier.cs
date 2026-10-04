namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveResponseVerifier
{
    internal static TimeSeriesIntensiveHash Validate(TimeSeriesIntensiveExpectations expected,
        TimeSeriesIntensivePreparedPhase phase, int index, TimeSeriesIntensiveResponse response)
    {
        var range = index % TimeSeriesIntensiveProfile.RangeGroups;
        string digest;
        switch (phase.Scenario)
        {
            case TimeSeriesIntensiveScenario.Append:
                phase.Appends!.Validate(index, response.Receipt!);
                digest = TimeSeriesIntensiveResultDigest.AppendReceipt(response.Receipt!);
                break;
            case TimeSeriesIntensiveScenario.RawRangeRead:
                digest = TimeSeriesIntensiveResultDigest.ValidatedRaw(expected.Raw[range], response.Raw);
                break;
            case TimeSeriesIntensiveScenario.Latest:
                digest = TimeSeriesIntensiveResultDigest.ValidatedLatest(expected.Latest[index % TimeSeriesIntensiveProfile.GroupCount], response.Latest);
                break;
            case TimeSeriesIntensiveScenario.Aggregate:
                TimeSeriesIntensiveOracle.ValidateAggregate(expected.Aggregates[range], response.Aggregate!);
                digest = TimeSeriesIntensiveResultDigest.Aggregate(response.Aggregate!);
                break;
            case TimeSeriesIntensiveScenario.Windows:
                TimeSeriesIntensiveOracle.ValidateWindows(expected.Windows[range], response.Windows);
                digest = TimeSeriesIntensiveResultDigest.Windows(response.Windows);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(phase));
        }

        return TimeSeriesIntensiveHash.Parse(digest);
    }
}
