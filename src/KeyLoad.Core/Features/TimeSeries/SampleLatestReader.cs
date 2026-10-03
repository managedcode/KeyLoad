using KeyLoad.Storage;

namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleLatestReader
{
    private const int OneSample = 1;

    internal static LatestSampleResult Read(DatabaseEngine database, IKeyValueView view, string principalId,
        ReadLatestSampleRequest request, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        var scope = SampleReadScope.Open(database, view, principalId, request.Partition, request.Set, request.SeriesId);
        var retention = SampleRetentionStateReader.Read(view, request.Partition, request.Set, request.SeriesId, budget);
        SampleRecord? selected = null;
        budget.Check();
        view.VisitReverseRange(scope.Prefix, OneSample, (_, value) =>
        {
            selected = Project(database, scope, value);
            return false;
        }, afterKey: retention is null ? null : SampleReadKeys.FromInclusive(request.Partition, request.Set,
            request.SeriesId, SampleRetentionStateReader.Before(retention)),
            untilKey: SampleReadKeys.ThroughInclusive(request.Partition, request.Set, request.SeriesId,
                request.AtOrBefore), cancellationToken: budget.Cancellation);
        budget.Check();
        return new(selected);
    }

    private static SampleRecord Project(DatabaseEngine database, SampleReadScope scope, ReadOnlySpan<byte> value)
    {
        var sample = NativeSerialization.Deserialize<SampleRecord>(value);
        return sample with
        {
            TagsJson = database.Authorization.Project(scope.Principal, scope.Resource.FieldPolicies,
                sample.TagsJson, out _)
        };
    }
}
