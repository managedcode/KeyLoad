namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal ReadExecutionBudget CreateRemoteGraphReadWork(DateTimeOffset expiresAt, CancellationToken token)
    {
        var work = new ReadExecutionBudget(OperationLimitsOptions, EvaluationClock, token);
        work.ConstrainLifetime(expiresAt);
        work.ConstrainResultBytes(checked((int)Math.Min((long)Limits.MaxBatchBytes, Limits.MaxQueryReadBytes)));
        work.Check();
        return work;
    }
}
