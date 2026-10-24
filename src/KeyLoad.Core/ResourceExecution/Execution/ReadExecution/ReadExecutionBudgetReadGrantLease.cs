namespace KeyLoad.Core.ResourceExecution.Execution;

/// <summary>Scopes an existing original allowance to one sequential execution reader without renewing it.</summary>
internal sealed class ReadExecutionBudgetReadGrantLease(ReadExecutionBudget owner, ReadExecutionBudgetReadGrant grant) : IDisposable
{
    private ReadExecutionBudget? current = owner;

    public void Dispose() => Interlocked.Exchange(ref current, null)?.ExitReadGrant(grant);
}
