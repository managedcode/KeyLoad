using KeyLoad.Core.Features.TimeSeries;

namespace KeyLoad.Core;
/// <summary>Executes SampleReads operations against the caller's database context.</summary>
public static class SampleReads
{
    internal const int DefaultSampleReadLimit = 1_000;
    /// <summary>Reads projected samples in an inclusive UTC timestamp range.</summary>
    /// <param name = "principalId">Persisted caller identity.</param>
    /// <param name = "partition">Atomic partition containing the series.</param>
    /// <param name = "set">Configured series set.</param>
    /// <param name = "seriesId">Series identifier.</param>
    /// <param name = "from">Inclusive UTC beginning.</param>
    /// <param name = "until">Inclusive UTC end.</param>
    /// <param name = "limit">Maximum returned samples.</param>
    /// <param name = "cancellationToken">Cancellation throughout storage and projection.</param>
    /// <returns>Owned projected samples in timestamp and sequence order.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static SampleRecord[] ReadSamples(this DatabaseEngine engine, string principalId, PartitionRef partition, string set, string seriesId, DateTimeOffset from, DateTimeOffset until, int limit = DefaultSampleReadLimit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var budget = new ReadExecutionBudget(engine.OperationLimitsOptions, engine.Clock, cancellationToken);
        budget.Check();
        var request = new ReadSamplesRequest(partition, set, seriesId, from, until, limit);
        return engine.Store.Read(view => SampleRangeReader.Read(engine, engine.Clock, view, principalId, request, budget));
    }
}
