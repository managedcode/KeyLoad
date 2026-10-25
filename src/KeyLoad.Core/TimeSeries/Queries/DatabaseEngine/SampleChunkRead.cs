namespace KeyLoad.Core;
/// <summary>Executes SampleChunkRead operations against the caller's database context.</summary>
public static class SampleChunkRead
{
    /// <summary>Reads the complete authorized enrolled chunk window at the original execution owner cut.</summary>

    /// <param name = "engine">The shared database execution context.</param>
    /// <param name = "principalId">The principal Id.</param>
    /// <param name = "request">The request.</param>
    /// <param name = "cancellationToken">The cancellation Token.</param>
    public static SampleChunkWindowResult ReadSampleChunkWindow(this DatabaseEngine engine, string principalId, ReadSampleChunkWindowRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(request);
        var budget = new ReadExecutionBudget(engine.Clock, cancellationToken);
        return engine.ReadSampleChunkWindowRead(principalId, request, budget);
    }

    internal static SampleChunkWindowResult ReadSampleChunkWindowRead(this DatabaseEngine engine, string principalId, ReadSampleChunkWindowRequest request, ReadExecutionBudget budget)
    {
        budget.Check();
        return engine.Store.Read(original =>
        {
            var grant = budget.CreateReadGrant(budget.RemainingReadGrantBytes, budget.RemainingReadGrantRecords);
            var view = budget.CreateView(original, grant);
            var result = TimeSeries.SampleChunkWindowReader.Read(engine, view, principalId, request, budget, engine.TimeSeriesOptions, engine.Store.Position);
            if (request.MinimumToken is { } minimum)
            {
                budget.Check();
                engine.ValidateDocumentSessionToken(view, request.Partition, minimum);
                budget.Check();
            }

            return result;
        });
    }
}
