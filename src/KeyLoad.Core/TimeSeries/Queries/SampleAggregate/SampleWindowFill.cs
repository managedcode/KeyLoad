using System.Collections.Immutable;

namespace KeyLoad.Core.Features.TimeSeries;

internal sealed class SampleWindowFill
{
    private readonly ReadExecutionBudget budget;
    private readonly long endTicks;
    private readonly long widthTicks;
    private readonly int windowCount;
    private readonly List<SampleAggregateWindow> windows;
    private SampleWindowAccumulator current;
    private long currentStartTicks;
    private long currentEndTicks;
    private int completed;

    internal SampleWindowFill(long fromTicks, long endTicks, long widthTicks, int windowCount,
        ReadExecutionBudget budget)
    {
        this.budget = budget;
        this.endTicks = endTicks;
        this.widthTicks = widthTicks;
        this.windowCount = windowCount;
        windows = new(windowCount);
        currentStartTicks = fromTicks;
        currentEndTicks = NextEnd(fromTicks, endTicks, widthTicks);
        current = new(fromTicks, currentEndTicks);
    }

    internal void Add(SampleRecord sample)
    {
        var ticks = sample.Sample.Timestamp.UtcTicks;
        while (ticks >= currentEndTicks && completed < windowCount)
        {
            CompleteCurrent();
            StartNext();
        }

        current.Add(sample);
    }

    internal ImmutableArray<SampleAggregateWindow> Complete()
    {
        budget.Check();
        while (completed < windowCount)
        {
            CompleteCurrent();
            if (completed < windowCount)
            {
                StartNext();
            }
        }

        budget.Check();
        return [.. windows];
    }

    private void CompleteCurrent()
    {
        budget.Check();
        windows.Add(current.Complete());
        completed++;
    }

    private void StartNext()
    {
        currentStartTicks = currentEndTicks;
        currentEndTicks = NextEnd(currentStartTicks, endTicks, widthTicks);
        current = new(currentStartTicks, currentEndTicks);
    }

    private static long NextEnd(long startTicks, long endTicks, long widthTicks)
    {
        var remaining = endTicks - startTicks;
        return startTicks + Math.Min(widthTicks, remaining);
    }
}
