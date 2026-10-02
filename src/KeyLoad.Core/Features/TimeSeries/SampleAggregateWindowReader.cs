using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleAggregateWindowReader
{
    private const string InvalidWidth = "The time-series window width must be positive.";
    private const string InvalidWindowBudget = "The time-series window budget is invalid.";
    private const string WindowBudgetExceeded = "The time-series window budget is exceeded.";
    private static readonly long AfterMaximumUtcTicks = DateTimeOffset.MaxValue.UtcTicks + 1;

    internal static SampleAggregateWindowsResult Read(DatabaseEngine database, IKeyValueView view,
        string principalId, AggregateSampleWindowsRequest request, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        SampleAggregateReader.ValidateRange(request.From, request.UntilExclusive);
        SampleAggregateReader.ValidateSampleLimit(request.MaxSamples, database.Limits.MaxScanRecords);
        ValidateWidth(request.Width);
        ValidateWindowLimit(request.MaxWindows, database.Limits.MaxResults);
        var scope = SampleReadScope.Open(database, view, principalId, request.Partition, request.Set, request.SeriesId);
        var endTicks = request.UntilExclusive?.UtcTicks ?? AfterMaximumUtcTicks;
        var windowCount = CountWindows(request.From.UtcTicks, endTicks, request.Width.Ticks);
        ValidateCalculatedCount(windowCount, request.MaxWindows, database.Limits.MaxResults);
        if (windowCount == 0)
        {
            budget.Check();
            return new(ImmutableArray<SampleAggregateWindow>.Empty);
        }

        var fill = new SampleWindowFill(request.From.UtcTicks, endTicks, request.Width.Ticks,
            checked((int)windowCount), budget);
        budget.Check();
        var scan = view.VisitRange(scope.Prefix, request.MaxSamples, (_, value) =>
        {
            budget.Check();
            fill.Add(SampleAggregateReader.ReadSample(value));
            return true;
        }, SampleReadKeys.FromInclusive(request.Partition, request.Set, request.SeriesId, request.From),
            SampleReadKeys.UntilExclusive(request.Partition, request.Set, request.SeriesId, request.UntilExclusive),
            cancellationToken: budget.Cancellation);
        if (scan.HasMore)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, SampleAggregateReader.SampleBudgetExceeded);
        }

        budget.Check();
        return new(fill.Complete());
    }

    private static void ValidateWidth(TimeSpan width)
    {
        if (width <= TimeSpan.Zero)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidWidth);
        }
    }

    private static void ValidateWindowLimit(int maxWindows, int serverMaximum)
    {
        if (maxWindows < 1 || maxWindows > serverMaximum)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, InvalidWindowBudget);
        }
    }

    private static long CountWindows(long fromTicks, long endTicks, long widthTicks)
    {
        var span = endTicks - fromTicks;
        var complete = span / widthTicks;
        return span % widthTicks == 0 ? complete : checked(complete + 1);
    }

    private static void ValidateCalculatedCount(long count, int callerMaximum, int serverMaximum)
    {
        if (count > callerMaximum || count > serverMaximum)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, WindowBudgetExceeded);
        }
    }
}

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
