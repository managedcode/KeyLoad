using KeyLoad.Core.Features.TimeSeries;
using KeyLoad.Storage;

namespace KeyLoad.Core;

/// <summary>Reads authorized time-series statistics within one node-local consistent cut.</summary>
public static class TimeSeriesReadOperations
{
    /// <summary>Reads the projected latest sample at an optional inclusive timestamp cut.</summary>
    /// <param name="database">Engine borrowing its node-local store.</param>
    /// <param name="principalId">Persisted caller identity.</param>
    /// <param name="request">Series identity and optional inclusive timestamp.</param>
    /// <param name="cancellationToken">Operation cancellation.</param>
    /// <returns>The latest owned sample, or a wrapper containing null when absent.</returns>
    public static LatestSampleResult ReadLatestSample(this DatabaseEngine database, string principalId,
        ReadLatestSampleRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Read(database, (view, budget) => SampleLatestReader.Read(database, view, principalId, request, budget), cancellationToken);
    }

    /// <summary>Reads complete raw statistics over a half-open UTC range.</summary>
    /// <param name="database">Engine borrowing its node-local store.</param>
    /// <param name="principalId">Persisted caller identity.</param>
    /// <param name="request">Series range and strict sample budget.</param>
    /// <param name="cancellationToken">Operation cancellation.</param>
    /// <returns>Complete finite statistics; sample-budget overflow rejects the operation.</returns>
    public static SampleAggregate AggregateSamples(this DatabaseEngine database, string principalId,
        AggregateSamplesRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Read(database, (view, budget) => SampleAggregateReader.Read(database, view, principalId, request, budget), cancellationToken);
    }

    /// <summary>Reads dense fixed-width UTC windows anchored at the requested beginning.</summary>
    /// <param name="database">Engine borrowing its node-local store.</param>
    /// <param name="principalId">Persisted caller identity.</param>
    /// <param name="request">Range, positive width and sample/window caps.</param>
    /// <param name="cancellationToken">Operation cancellation.</param>
    /// <returns>Owned immutable dense windows after complete input/result budget checks.</returns>
    public static SampleAggregateWindowsResult AggregateSampleWindows(this DatabaseEngine database, string principalId,
        AggregateSampleWindowsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Read(database, (view, budget) => SampleAggregateWindowReader.Read(database, view, principalId, request, budget), cancellationToken);
    }

    private static T Read<T>(DatabaseEngine database, Func<IKeyValueView, ReadExecutionBudget, T> read,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        var budget = new ReadExecutionBudget(database.Limits, database.EvaluationClock, cancellationToken);
        budget.Check();
        return database.Store.Read(view =>
        {
            var result = read(budget.CreateView(view), budget);
            budget.CheckResult(result);
            return result;
        });
    }
}
