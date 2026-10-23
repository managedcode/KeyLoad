namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensivePhaseFrames
{
    internal static void Write(TimeSeriesIntensiveDigestWriter frame)
    {
        const int NoObservedItems = 0;

        frame.String(TimeSeriesIntensiveFrameLabels.PhasePlans);
        frame.Count(TimeSeriesIntensiveProfile.RepetitionCount * TimeSeriesIntensiveProfile.OperationCount);
        for (var repetition = NoObservedItems; repetition < TimeSeriesIntensiveProfile.RepetitionCount; repetition++)
        {
            Phase(frame, repetition);
        }
    }

    private static void Phase(TimeSeriesIntensiveDigestWriter frame, int repetition)
    {
        const int FirstElementIndex = 0;

        var count = TimeSeriesIntensiveProfile.OperationCount;
        var series = TimeSeriesIntensivePlans.PhaseSeries(repetition);
        for (var index = FirstElementIndex; index < count; index++)
        {
            frame.Field(TimeSeriesIntensiveFrameLabels.Repetition, repetition);
            frame.Field(TimeSeriesIntensiveFrameLabels.Phase, TimeSeriesIntensiveProfile.MeasuredPhase);
            frame.Field(TimeSeriesIntensiveFrameLabels.SeriesId, series);
            frame.Field(TimeSeriesIntensiveFrameLabels.Index, index);
            frame.Field(TimeSeriesIntensiveFrameLabels.CommandPurpose, TimeSeriesIntensivePlans.CommandPurpose(repetition, index));
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
