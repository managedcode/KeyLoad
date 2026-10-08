namespace KeyLoad.Core;

internal sealed class ReadExecutionBudgetStageCancellation
{
    private const string StageAlreadyOwned = "The read budget already has an owned cancellation stage.";
    private const string StageOwnerMismatch = "The read budget cancellation stage belongs to another owner.";
    private readonly Lock gate = new();
    private ReadExecutionBudgetStageCancellationLease? current;

    internal ReadExecutionBudgetStageCancellationLease Enter(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (current is not null)
            { throw new InvalidOperationException(StageAlreadyOwned); }
            var lease = new ReadExecutionBudgetStageCancellationLease(this, token);
            Volatile.Write(ref current, lease);
            return lease;
        }
    }

    internal void Check() => Volatile.Read(ref current)?.Token.ThrowIfCancellationRequested();

    internal void Release(ReadExecutionBudgetStageCancellationLease lease)
    {
        lock (gate)
        {
            if (!ReferenceEquals(current, lease))
            { throw new InvalidOperationException(StageOwnerMismatch); }
            Volatile.Write(ref current, null);
        }
    }
}
