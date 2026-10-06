using KeyLoad.Storage;

namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleAggregateReader
{
    private const int MinimumRequestedSamples = 1;

    private const string InvalidRange = "The time-series aggregate range is invalid.";
    private const string InvalidSampleBudget = "The time-series sample budget is invalid.";
    internal const string SampleBudgetExceeded = "The time-series sample budget is exceeded.";

    internal static SampleAggregate Read(DatabaseEngine database, IKeyValueView view, string principalId,
        AggregateSamplesRequest request, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ValidateRange(request.From, request.UntilExclusive);
        ValidateSampleLimit(request.MaxSamples, database.Limits.MaxScanRecords);
        var scope = SampleReadScope.Open(database, view, principalId, request.Partition, request.Set, request.SeriesId);
        var retention = SampleRetentionStateReader.Read(view, request.Partition, request.Set, request.SeriesId, budget);
        var from = retention is not null && retention.BeforeUtcTicks > request.From.UtcTicks
            ? SampleRetentionStateReader.Before(retention) : request.From;
        if (request.UntilExclusive is { } requestedUntil && from >= requestedUntil)
        {
            budget.Check();
            return SampleAggregateAccumulator.Empty;
        }
        if (request.UntilExclusive is { } until && request.From.UtcTicks == until.UtcTicks)
        {
            budget.Check();
            return SampleAggregateAccumulator.Empty;
        }

        var accumulator = new SampleAggregateAccumulator();
        budget.Check();
        var scan = view.VisitRange(scope.Prefix, request.MaxSamples, (_, value) =>
        {
            budget.Check();
            accumulator.Add(ReadSample(value));
            return true;
        }, SampleReadKeys.FromInclusive(request.Partition, request.Set, request.SeriesId, from),
            SampleReadKeys.UntilExclusive(request.Partition, request.Set, request.SeriesId, request.UntilExclusive),
            cancellationToken: budget.Cancellation);
        if (scan.HasMore)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, SampleBudgetExceeded);
        }

        budget.Check();
        return accumulator.Complete();
    }

    internal static void ValidateRange(DateTimeOffset from, DateTimeOffset? untilExclusive)
    {
        if (untilExclusive is { } until && from > until)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRange);
        }
    }

    internal static void ValidateSampleLimit(int maxSamples, int serverMaximum)
    {
        if (maxSamples < MinimumRequestedSamples || maxSamples > serverMaximum)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, InvalidSampleBudget);
        }
    }

    internal static SampleRecord ReadSample(ReadOnlySpan<byte> value) => NativeSerialization.Deserialize<SampleRecord>(value);
}
