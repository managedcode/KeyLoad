using KeyLoad.Storage;

namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleRangeReader
{
    private const string SampleReadKeySpace = "sample";
    private const string InvalidSampleReadBudget = "The series read budget is invalid.";
    private const string SampleResultBudgetExceeded = "The series result byte budget is exceeded.";
    private const int NextTimestampTick = 1;

    internal static SampleRecord[] Read(DatabaseEngine database, TimeProvider clock, IKeyValueView view,
        string principalId, ReadSamplesRequest request, ReadExecutionBudget budget)
    {
        if (request.From > request.Until || request.Limit < 1 || request.Limit > database.Limits.MaxResults)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, InvalidSampleReadBudget);
        }
        var principal = database.Principal(view, principalId, clock.GetUtcNow());
        database.Authorization.Require(principal, request.Partition, request.Set, Capability.SeriesRead);
        var resource = database.Resource(view, request.Partition, request.Set, ResourceKind.TimeSeries);
        var retention = SampleRetentionStateReader.Read(view, request.Partition, request.Set, request.SeriesId, budget);
        var from = retention is not null && retention.BeforeUtcTicks > request.From.UtcTicks
            ? SampleRetentionStateReader.Before(retention) : request.From;
        var prefix = KeySpace.Partition(SampleReadKeySpace, request.Partition, request.Set, request.SeriesId);
        var after = KeySpace.Partition(SampleReadKeySpace, request.Partition, request.Set, request.SeriesId, from);
        var until = SampleUpperBound(request);
        var records = new List<SampleRecord>();
        var resultBytes = 0L;
        budget.VisitRange(view, prefix, request.Limit, (key, value) =>
        {
            var sample = NativeSerialization.Deserialize<SampleRecord>(value);
            var projected = sample with { TagsJson = database.Authorization.Project(principal, resource.FieldPolicies, sample.TagsJson, out _) };
            resultBytes += budget.MeasureResult(projected);
            if (resultBytes > database.Limits.MaxBatchBytes)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, SampleResultBudgetExceeded);
            }
            records.Add(projected);
            return true;
        }, after, until);
        var result = records.ToArray();
        budget.CheckResult(result);
        return result;
    }

    private static byte[]? SampleUpperBound(ReadSamplesRequest request)
        => request.Until.UtcTicks == DateTimeOffset.MaxValue.UtcTicks ? null
            : KeySpace.Partition(SampleReadKeySpace, request.Partition, request.Set, request.SeriesId,
                new DateTimeOffset(request.Until.UtcTicks + NextTimestampTick, TimeSpan.Zero));
}
