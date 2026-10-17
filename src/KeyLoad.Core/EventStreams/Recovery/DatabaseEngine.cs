namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    /// <summary>Reads one compatible snapshot and its complete retained event tail under one authorized cut.</summary>
    /// <param name="principalId">The persisted worker principal.</param>
    /// <param name="request">The exact stream, reducer/schema identity and complete-tail limit.</param>
    /// <param name="cancellationToken">Cancellation for the bounded same-cut read.</param>
    /// <returns>The compatible state and every following raw event through the captured head.</returns>
    public AggregateReplayPage ReadAggregateReplay(string principalId, ReadAggregateReplayRequest request,
        CancellationToken cancellationToken = default)
    {
        var budget = new ReadExecutionBudget(OperationLimitsOptions, Clock, cancellationToken);
        budget.Check();
        return Store.Read(view => AggregateReplayReader.Read(this, view, principalId, request, budget));
    }
}
