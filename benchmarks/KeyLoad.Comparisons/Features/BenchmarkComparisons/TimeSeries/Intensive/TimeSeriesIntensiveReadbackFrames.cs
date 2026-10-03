namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveReadbackFrames
{
    internal static void Write(TimeSeriesIntensiveDigestWriter frame)
    {
        frame.String(TimeSeriesIntensiveFrameLabels.SeedReadbacks);
        frame.Count(TimeSeriesIntensiveProfile.SeedBatchCount);
        foreach (var readback in TimeSeriesIntensivePlans.SeedReadbacks())
        {
            Readback(frame, readback);
        }

        frame.String(TimeSeriesIntensiveFrameLabels.WarmupReadbacks);
        frame.Count(TimeSeriesIntensiveProfile.RepetitionCount);
        for (var repetition = 0; repetition < TimeSeriesIntensiveProfile.RepetitionCount; repetition++)
        {
            PhaseReadbacks(frame, repetition, true);
        }

        frame.String(TimeSeriesIntensiveFrameLabels.MeasuredReadbacks);
        frame.Count(TimeSeriesIntensiveProfile.RepetitionCount * TimeSeriesIntensiveProfile.AppendReadbackCount);
        for (var repetition = 0; repetition < TimeSeriesIntensiveProfile.RepetitionCount; repetition++)
        {
            PhaseReadbacks(frame, repetition, false);
        }
    }

    private static void PhaseReadbacks(TimeSeriesIntensiveDigestWriter frame, int repetition, bool warmup)
    {
        foreach (var readback in TimeSeriesIntensivePlans.AppendReadbacks(repetition, warmup))
        {
            Readback(frame, readback);
        }
    }

    private static void Readback(TimeSeriesIntensiveDigestWriter frame, TimeSeriesIntensiveReadback readback)
    {
        frame.Field(TimeSeriesIntensiveFrameLabels.SeriesId, readback.SeriesId);
        frame.Field(TimeSeriesIntensiveFrameLabels.FromUtcTicks, readback.From.UtcTicks);
        frame.Field(TimeSeriesIntensiveFrameLabels.UntilUtcTicks, readback.Until.UtcTicks);
        frame.Field(TimeSeriesIntensiveFrameLabels.ExpectedCount, readback.ExpectedCount);
    }
}
