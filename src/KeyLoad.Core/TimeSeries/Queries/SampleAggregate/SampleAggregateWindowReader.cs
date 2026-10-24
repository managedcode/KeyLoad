using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core.TimeSeries;

internal static class SampleAggregateWindowReader
{
    private const string InvalidWidth = "The time-series window width must be positive.";
    private const string InvalidWindowBudget = "The time-series window budget is invalid.";
    private const string WindowBudgetExceeded = "The time-series window budget is exceeded.";
    private static readonly long AfterMaximumUtcTicks = DateTimeOffset.MaxValue.UtcTicks + 1;

    internal static SampleAggregateWindowsResult Read(DatabaseEngine database, IKeyValueView view,
        string principalId, AggregateSampleWindowsRequest request, ReadExecutionBudget budget)
    {
        const int EmptyWindowCount = 0;

        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        SampleAggregateReader.ValidateRange(request.From, request.UntilExclusive);
        SampleAggregateReader.ValidateSampleLimit(request.MaxSamples, database.Limits.MaxScanRecords);
        ValidateWidth(request.Width);
        ValidateWindowLimit(request.MaxWindows, database.Limits.MaxResults);
        var scope = SampleReadScope.Open(database, view, principalId, request.Partition, request.Set, request.SeriesId);
        var retention = SampleRetentionStateReader.Read(view, request.Partition, request.Set, request.SeriesId, budget);
        var readFrom = retention is not null && retention.BeforeUtcTicks > request.From.UtcTicks
            ? SampleRetentionStateReader.Before(retention) : request.From;
        var endTicks = request.UntilExclusive?.UtcTicks ?? AfterMaximumUtcTicks;
        var windowCount = CountWindows(request.From.UtcTicks, endTicks, request.Width.Ticks);
        ValidateCalculatedCount(windowCount, request.MaxWindows, database.Limits.MaxResults);
        if (windowCount == EmptyWindowCount)
        {
            budget.Check();
            return new(ImmutableArray<SampleAggregateWindow>.Empty);
        }

        var fill = new SampleWindowFill(request.From.UtcTicks, endTicks, request.Width.Ticks,
            checked((int)windowCount), budget);
        budget.Check();
        var scan = view.VisitRange(scope.Prefix, request.MaxSamples, (key, value) =>
        {
            budget.Check();
            fill.Add(SampleRawReadDecoder.Read(key, value,
                request.Partition, request.Set, request.SeriesId));
            return true;
        }, SampleReadKeys.FromInclusive(request.Partition, request.Set, request.SeriesId, readFrom),
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
        const int EmptySpanWidthTicks = 0;
        const int CompleteStep = 1;

        var span = endTicks - fromTicks;
        var complete = span / widthTicks;
        return span % widthTicks == EmptySpanWidthTicks ? complete : checked(complete + CompleteStep);
    }

    private static void ValidateCalculatedCount(long count, int callerMaximum, int serverMaximum)
    {
        if (count > callerMaximum || count > serverMaximum)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, WindowBudgetExceeded);
        }
    }
}
