using KeyLoad.Core.ResourceExecution;
using KeyLoad.Core.TimeSeries;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class TimeSeriesBoundReadOperations
{
    internal static LatestSampleResult ReadLatestSampleBound(this DatabaseEngine database, string principalId,
        ReadLatestSampleRequest request, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Read(database, budget, binding,
            view => SampleLatestReader.Read(database, view, principalId, request, budget));
    }

    internal static SampleAggregate AggregateSamplesBound(this DatabaseEngine database, string principalId,
        AggregateSamplesRequest request, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Read(database, budget, binding,
            view => SampleAggregateReader.Read(database, view, principalId, request, budget));
    }

    internal static SampleAggregateWindowsResult AggregateSampleWindowsBound(this DatabaseEngine database, string principalId,
        AggregateSampleWindowsRequest request, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Read(database, budget, binding,
            view => SampleAggregateWindowReader.Read(database, view, principalId, request, budget));
    }

    internal static SampleRetentionStatus ReadSampleRetentionBound(this DatabaseEngine database, string principalId,
        ReadSampleRetentionRequest request, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Read(database, budget, binding,
            view => SampleRetentionStatusReader.Read(database, view, principalId, request, budget));
    }

    internal static SampleRollupResult ReadSampleRollupBound(this DatabaseEngine database, string principalId,
        ReadSampleRollupRequest request, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Read(database, budget, binding,
            view => SampleRollupReader.Read(database, view, principalId, request, budget));
    }

    internal static SampleRecord[] ReadSamplesBound(this DatabaseEngine database, string principalId,
        ReadSamplesRequest request, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        budget.Check();
        return database.Store.Read(view => SampleRangeReader.Read(database, database.EvaluationClock,
            view, principalId, request, budget));
    }

    private static T Read<T>(DatabaseEngine database, ReadExecutionBudget budget,
        IResourceLifetimeWorkBinding binding, Func<IKeyValueView, T> read)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        budget.Check();
        return database.Store.Read(raw =>
        {
            var result = read(budget.CreateView(raw));
            budget.CheckResult(result);
            return result;
        });
    }
}
