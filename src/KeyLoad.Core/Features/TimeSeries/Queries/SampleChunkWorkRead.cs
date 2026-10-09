namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkWorkRead
{
    internal static SampleChunkWorkHint? Current(DatabaseEngine database, SampleChunkWorkHint original,
        CancellationToken cancellationToken)
    {
        var budget = new ReadExecutionBudget(database.OperationLimitsOptions, database.EvaluationClock, cancellationToken);
        budget.Check();
        return database.Store.Read(view => SampleChunkWorkEligibility.Read(database, budget.CreateView(view),
            original.Partition, original.Set, original.Series, original.WindowId, database.EvaluationClock.GetUtcNow(), budget));
    }
}
