using KeyLoad.Storage;

namespace KeyLoad.Core.TimeSeries;

internal static class SampleRangeReader
{
    private const string SampleReadKeySpace = "sample";
    private const string InvalidSampleReadBudget = "The series read budget is invalid.";
    private const int NextTimestampTick = 1;

    internal static SampleRecord[] Read(DatabaseEngine database, TimeProvider clock, IKeyValueView view,
        string principalId, ReadSamplesRequest request, ReadExecutionBudget budget)
    {
        const int LimitValidationBoundary = 1;

        if (request.From > request.Until || request.Limit < LimitValidationBoundary)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, InvalidSampleReadBudget);
        }
        var metadata = budget.CreateView(view);
        var principal = database.Principal(metadata, principalId, clock.GetUtcNow());
        database.Authorization.Require(principal, request.Partition, request.Set, Capability.SeriesRead);
        var resource = ResourceLifetimeReadBinding.BindAuthorizedReadResource(metadata, request.Partition, request.Set, ResourceKind.TimeSeries);
        var retention = (SampleRetentionStateReader.Read(view, request.Partition, request.Set, request.SeriesId, budget)).Value;
        var from = retention is not null && retention.BeforeUtcTicks > request.From.UtcTicks
            ? SampleRetentionStateReader.Before(retention) : request.From;
        var prefix = KeySpace.Partition(SampleReadKeySpace, request.Partition, request.Set, request.SeriesId);
        var after = KeySpace.Partition(SampleReadKeySpace, request.Partition, request.Set, request.SeriesId, from);
        var until = (SampleUpperBound(request)).Value;
        var records = new List<SampleRecord>();
        _ = budget.VisitAll(view, prefix, (key, value) =>
        {
            var sample = SampleRawReadDecoder.Read(key, value,
                request.Partition, request.Set, request.SeriesId);
            var projected = sample with
            {
                TagsJson = database.Authorization.Project(principal, resource.FieldPolicies, sample.TagsJson, out _)
            };
            records.Add(projected);
            return records.Count < request.Limit;
        }, after, until);
        var result = records.ToArray();
        return result;
    }

    private static (bool HasValue, byte[]? Value) SampleUpperBound(ReadSamplesRequest request)
        => (request.Until.UtcTicks == DateTimeOffset.MaxValue.UtcTicks ? null
            : KeySpace.Partition(SampleReadKeySpace, request.Partition, request.Set, request.SeriesId,
                new DateTimeOffset(request.Until.UtcTicks + NextTimestampTick, TimeSpan.Zero))) is { } outcomeValue1 ? (true, outcomeValue1) : (false, default(byte[]?));
}
