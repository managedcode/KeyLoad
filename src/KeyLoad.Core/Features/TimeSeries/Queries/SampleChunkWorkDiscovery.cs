using System.Collections.Immutable;
using KeyLoad.Storage;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkWorkDiscovery
{
    internal static SampleChunkWorkPage Read(DatabaseEngine database, byte[]? afterKey,
        CancellationToken cancellationToken)
    {
        var budget = new ReadExecutionBudget(database.OperationLimitsOptions, database.EvaluationClock, cancellationToken);
        budget.Check();
        var hints = ImmutableArray.CreateBuilder<SampleChunkWorkHint>();
        byte[]? last = null;
        return database.Store.Read(view =>
        {
            var owned = budget.CreateView(view);
            var scan = owned.VisitRange(KeyCodec.Encode(PartitionRecordFamilies.SampleChunkWindow),
                database.TimeSeriesOptions.Value.MaximumPendingChunkWindows, (key, _) =>
                {
                    budget.Check();
                    var scope = SampleChunkWorkKey.Decode(key);
                    last = key.ToArray();
                    try
                    {
                        var hint = SampleChunkWorkEligibility.Read(database, owned, scope.Partition, scope.Set,
                            scope.Series, scope.Id, database.EvaluationClock.GetUtcNow(), budget);
                        if (hint is not null) { hints.Add(hint); }
                    }
                    catch (KeyLoadException failure) when (failure.Code is ErrorCode.Unauthenticated or ErrorCode.PermissionDenied)
                    { return true; }
                    return true;
                }, afterKey: afterKey, cancellationToken: cancellationToken);
            budget.Check();
            return new SampleChunkWorkPage(hints.ToImmutable(), scan.HasMore ? last : null, scan.HasMore);
        });
    }
}
