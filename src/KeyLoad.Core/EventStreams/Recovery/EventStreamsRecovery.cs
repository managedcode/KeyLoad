namespace KeyLoad.Core;
/// <summary>Executes EventStreamsRecovery operations against the caller's database context.</summary>
public static class EventStreamsRecovery
{
    /// <summary>Reads one compatible snapshot and its complete retained event tail under one authorized cut.</summary>
    /// <param name = "principalId">The persisted worker principal.</param>
    /// <param name = "request">The exact stream, reducer/schema identity and complete-tail limit.</param>
    /// <param name = "cancellationToken">Cancellation for the bounded same-cut read.</param>
    /// <returns>The compatible state and every following raw event through the captured head.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static AggregateReplayPage ReadAggregateReplay(this DatabaseEngine engine, string principalId, ReadAggregateReplayRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var budget = new ReadExecutionBudget(engine.Clock, cancellationToken);
        budget.Check();
        return engine.Store.Read(view => AggregateReplayReader.Read(engine, view, principalId, request, budget));
    }
}
