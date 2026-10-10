namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal ReadExecutionBudget CreateEventVectorReadWork(DateTimeOffset originalRequestExpiry,
        CancellationToken originalCancellationToken)
    {
        var work = new ReadExecutionBudget(OperationLimitsOptions, EvaluationClock, originalCancellationToken);
        work.ConstrainLifetime(originalRequestExpiry);
        work.ConstrainResultBytes(checked((int)Math.Min(Limits.MaxBatchBytes, Limits.MaxQueryReadBytes)));
        work.Check();
        return work;
    }
}
