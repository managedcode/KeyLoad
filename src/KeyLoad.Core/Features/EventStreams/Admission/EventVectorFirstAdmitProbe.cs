using KeyLoad.Core.Features.ResourceExecution.Execution;

namespace KeyLoad.Core;

/// <summary>Owns the existing read grant for one verified first-Admit journal inspection.</summary>
public sealed class EventVectorFirstAdmitProbe : IDisposable
{
    private readonly ReadExecutionBudget budget;
    private readonly ReadExecutionBudgetReadGrant grant;
    private bool disposed;

    internal EventVectorFirstAdmitProbe(ReadExecutionBudget budget, ReadExecutionBudgetReadGrant grant)
    { this.budget = budget; this.grant = grant; }

    /// <summary>Gets the canonical applied position read in the authorized bounded view.</summary>
    public long AppliedPosition { get; internal set; }
    /// <summary>Gets the original request ID from the same verified and authorized first-Admit phase.</summary>
    public Guid OriginalParentRequestId { get; internal set; }
    /// <summary>Gets the genuine reauthorized immutable outcome, if already committed.</summary>
    public OperationResult? OriginalOutcome { get; internal set; }

    /// <summary>Checks the original caller and original expiry before more journal work.</summary>
    public void Check() { ObjectDisposedException.ThrowIf(disposed, this); budget.Check(); }

    /// <summary>Charges one actual stored native key/value lookup before copy or decode, including absence.</summary>
    /// <param name="encodedBytes">The storage observer's actual encoded key/value byte count.</param>
    public void AcceptNativeRecord(long encodedBytes)
    { Check(); budget.ChargeReadGrant(grant, encodedBytes); }

    internal void DisposeAfterFailure(Exception primary)
    {
        try
        { Dispose(); }
        catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        { return; }
        budget.CompleteSettledReadGrant(grant);
        disposed = true;
    }
}
