namespace KeyLoad.Core;

internal sealed class ReadExecutionBudgetStageCancellationLease : IDisposable
{
    private ReadExecutionBudgetStageCancellation? owner;

    internal ReadExecutionBudgetStageCancellationLease(ReadExecutionBudgetStageCancellation owner,
        CancellationToken token)
    {
        this.owner = owner;
        Token = token;
    }

    internal CancellationToken Token { get; }

    public void Dispose() => Interlocked.Exchange(ref owner, null)?.Release(this);
}
