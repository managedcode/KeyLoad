namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensivePhaseFrames
{
    internal static void Write(TimeSeriesIntensiveDigestWriter frame)
    {
        frame.String(TimeSeriesIntensiveFrameLabels.PhasePlans);
        frame.Count(TimeSeriesIntensiveProfile.RepetitionCount * (TimeSeriesIntensiveProfile.WarmupCount + TimeSeriesIntensiveProfile.OperationCount));
        for (var repetition = 0; repetition < TimeSeriesIntensiveProfile.RepetitionCount; repetition++)
        {
            Phase(frame, repetition, true);
            Phase(frame, repetition, false);
        }
    }

    private static void Phase(TimeSeriesIntensiveDigestWriter frame, int repetition, bool warmup)
    {
        var count = warmup ? TimeSeriesIntensiveProfile.WarmupCount : TimeSeriesIntensiveProfile.OperationCount;
        var series = TimeSeriesIntensivePlans.PhaseSeries(repetition, warmup);
        for (var index = 0; index < count; index++)
        {
            frame.Field(TimeSeriesIntensiveFrameLabels.Repetition, repetition);
            frame.Field(TimeSeriesIntensiveFrameLabels.Phase, warmup ? TimeSeriesIntensiveProfile.WarmupPhase : TimeSeriesIntensiveProfile.MeasuredPhase);
            frame.Field(TimeSeriesIntensiveFrameLabels.SeriesId, series);
            frame.Field(TimeSeriesIntensiveFrameLabels.Index, index);
            frame.Field(TimeSeriesIntensiveFrameLabels.CommandPurpose, TimeSeriesIntensivePlans.CommandPurpose(repetition, warmup, index));
            frame.String(TimeSeriesIntensiveFrameLabels.AppendSample);
            TimeSeriesIntensiveResultFrames.AppendSample(frame, TimeSeriesIntensiveCorpus.AppendSample(index));
            Queries(frame, TimeSeriesIntensivePlans.Read(index));
        }
    }

    private static void Queries(TimeSeriesIntensiveDigestWriter frame, TimeSeriesIntensiveReadPlan plan)
    {
        frame.Field(TimeSeriesIntensiveFrameLabels.RawFromUtcTicks, plan.From.UtcTicks);
        frame.Field(TimeSeriesIntensiveFrameLabels.RawUntilUtcTicks, plan.Until.UtcTicks);
        frame.Field(TimeSeriesIntensiveFrameLabels.LatestAtOrBeforeUtcTicks, plan.LatestAtOrBefore.UtcTicks);
        frame.Field(TimeSeriesIntensiveFrameLabels.AggregateFromUtcTicks, plan.From.UtcTicks);
        frame.Field(TimeSeriesIntensiveFrameLabels.AggregateUntilExclusiveUtcTicks, plan.Until.UtcTicks);
        frame.Field(TimeSeriesIntensiveFrameLabels.WindowsFromUtcTicks, plan.From.UtcTicks);
        frame.Field(TimeSeriesIntensiveFrameLabels.WindowsUntilExclusiveUtcTicks, plan.Until.UtcTicks);
    }
}
