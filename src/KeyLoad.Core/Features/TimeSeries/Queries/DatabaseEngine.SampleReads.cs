using KeyLoad.Core.Features.Search;
using KeyLoad.Core.Features.TimeSeries;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    /// <summary>Reads projected samples in an inclusive UTC timestamp range.</summary>
    /// <param name="principalId">Persisted caller identity.</param>
    /// <param name="partition">Atomic partition containing the series.</param>
    /// <param name="set">Configured series set.</param>
    /// <param name="seriesId">Series identifier.</param>
    /// <param name="from">Inclusive UTC beginning.</param>
    /// <param name="until">Inclusive UTC end.</param>
    /// <param name="limit">Maximum returned samples.</param>
    /// <param name="cancellationToken">Cancellation throughout storage and projection.</param>
    /// <returns>Owned projected samples in timestamp and sequence order.</returns>
    public SampleRecord[] ReadSamples(string principalId, PartitionRef partition, string set, string seriesId,
        DateTimeOffset from, DateTimeOffset until, int limit = 1_000, CancellationToken cancellationToken = default)
    {
        var budget = new ReadExecutionBudget(Limits, Clock, cancellationToken);
        budget.Check();
        var request = new ReadSamplesRequest(partition, set, seriesId, from, until, limit);
        return Store.Read(view => SampleRangeReader.Read(this, Clock, view, principalId, request, budget));
    }
}
