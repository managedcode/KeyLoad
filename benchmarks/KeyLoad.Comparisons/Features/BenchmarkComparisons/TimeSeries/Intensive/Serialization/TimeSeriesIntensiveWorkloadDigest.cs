namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveWorkloadDigest
{
    internal static string Compute()
    {
        using var frame = new TimeSeriesIntensiveDigestWriter(TimeSeriesIntensiveFrameLabels.WorkloadDomain);
        Header(frame);
        frame.String(TimeSeriesIntensiveFrameLabels.SeedInsertion);
        frame.Count(TimeSeriesIntensiveProfile.SampleCount);
        foreach (var sample in TimeSeriesIntensiveCorpus.SeedInsertion)
        {
            TimeSeriesIntensiveResultFrames.Sample(frame, sample, TimeSeriesIntensiveProfile.Tags);
        }

        TimeSeriesIntensivePhaseFrames.Write(frame);
        TimeSeriesIntensiveReadbackFrames.Write(frame);
        return frame.Finish();
    }

    private static void Header(TimeSeriesIntensiveDigestWriter frame)
    {
        frame.Field(TimeSeriesIntensiveFrameLabels.Profile, TimeSeriesIntensiveProfile.Name);
        frame.Field(TimeSeriesIntensiveFrameLabels.RandomSeed, TimeSeriesIntensiveProfile.RandomSeed);
        frame.Field(TimeSeriesIntensiveFrameLabels.EpochUtcTicks, TimeSeriesIntensiveProfile.Epoch.UtcTicks);
        frame.Field(TimeSeriesIntensiveFrameLabels.SampleCount, TimeSeriesIntensiveProfile.SampleCount);
        frame.Field(TimeSeriesIntensiveFrameLabels.OperationCount, TimeSeriesIntensiveProfile.OperationCount);
        frame.Field(TimeSeriesIntensiveFrameLabels.WarmupCount, TimeSeriesIntensiveProfile.WarmupCount);
        frame.Field(TimeSeriesIntensiveFrameLabels.RepetitionCount, TimeSeriesIntensiveProfile.RepetitionCount);
        frame.Field(TimeSeriesIntensiveFrameLabels.Concurrency, TimeSeriesIntensiveProfile.Concurrency);
        frame.Field(TimeSeriesIntensiveFrameLabels.OperationTimeoutTicks, TimeSeriesIntensiveProfile.OperationTimeout.Ticks);
        frame.Field(TimeSeriesIntensiveFrameLabels.RawLimit, TimeSeriesIntensiveProfile.RawLimit);
        frame.Field(TimeSeriesIntensiveFrameLabels.MaxSamples, TimeSeriesIntensiveProfile.MaxSamples);
        frame.Field(TimeSeriesIntensiveFrameLabels.MaxWindows, TimeSeriesIntensiveProfile.MaxWindows);
        frame.Field(TimeSeriesIntensiveFrameLabels.WindowWidthTicks, TimeSeriesIntensiveProfile.WindowWidth.Ticks);
        frame.Field(TimeSeriesIntensiveFrameLabels.AverageToleranceBits, BitConverter.DoubleToInt64Bits(TimeSeriesIntensiveProfile.AverageTolerance));
    }
}
