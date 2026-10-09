using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution.Execution;

/// <summary>Owns scoped read-grant reservations on the same operation ledger, lifetime and cancellation state.</summary>
internal sealed class ReadExecutionBudgetScopedReadGrants(ReadExecutionBudget budget)
{
    private const int EmptyElementCount = 0;
    private const string ExaminedRecordsExceeded = "The read execution examined-record budget is exceeded.";

    private const string GrantOwnerMismatch = "The read grant belongs to a different operation.";

    private long reservedReadGrantBytes;

    private int examinedGrantRecords;

    private int reservedGrantRecords;

    private ReadExecutionBudgetReadGrant? scopedReadGrant;

    internal long ReservedBytes => reservedReadGrantBytes;
    internal ReadExecutionBudgetReadGrant? Current => scopedReadGrant;

    internal int ClaimedRecords => examinedGrantRecords + reservedGrantRecords;

    internal void CompleteReadGrant(ReadExecutionBudgetReadGrant grant)
    {
        budget.Check();
        ArgumentNullException.ThrowIfNull(grant);
        if (!grant.BelongsTo(budget) || grant.IsCompleted)
        { throw new ArgumentException(GrantOwnerMismatch, nameof(grant)); }
        reservedReadGrantBytes -= grant.RemainingBytes;
        reservedGrantRecords -= grant.RemainingRecords;
        grant.Complete();
    }

    internal ReadExecutionBudgetReadGrantLease EnterReadGrant(ReadExecutionBudgetReadGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        budget.Check();
        if (!grant.BelongsTo(budget) || grant.IsCompleted || scopedReadGrant is not null)
        { throw new ArgumentException(GrantOwnerMismatch, nameof(grant)); }
        scopedReadGrant = grant;
        return new(budget, grant);
    }

    internal void ExitReadGrant(ReadExecutionBudgetReadGrant grant)
    {
        if (!ReferenceEquals(scopedReadGrant, grant))
        { throw new ArgumentException(GrantOwnerMismatch, nameof(grant)); }
        scopedReadGrant = null;
    }

    internal void ChargeNativeReadRecord(long count)
    {
        if (scopedReadGrant is { } grant)
        { ChargeReadGrant(grant, count); }
        else
        { budget.ChargeBytes(count); }
    }

    internal IKeyValueView CreateView(IKeyValueView view, ReadExecutionBudgetReadGrant grant)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(grant);
        budget.Check();
        if (!grant.BelongsTo(budget) || grant.IsCompleted
            || scopedReadGrant is not null && !ReferenceEquals(scopedReadGrant, grant))
        { throw new ArgumentException(GrantOwnerMismatch, nameof(grant)); }
        return new BudgetedReadView(view, budget, grant);
    }

    /// <summary>Reserves one non-borrowable raw-byte ceiling for a sequential query leaf.</summary>
    /// <param name="maximumBytes">Maximum accepted native bytes reserved for that leaf.</param>
    /// <param name="maximumRecords">Maximum native point attempts and range records reserved for that leaf.</param>
    /// <returns>A reader sharing this operation's aggregate bytes, deadline and cancellation.</returns>
    internal ReadExecutionBudgetReadGrant CreateReadGrant(long maximumBytes, int maximumRecords)
    {
        budget.Check();
        ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumRecords);
        if (maximumBytes > budget.MaximumNativeReadBytes - budget.ReadBytes - reservedReadGrantBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ReadExecutionBudget.ReadBytesExceeded);
        }
        if (maximumRecords > budget.MaximumNativeScanRecords - examinedGrantRecords - reservedGrantRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ExaminedRecordsExceeded);
        }
        reservedReadGrantBytes += maximumBytes;
        reservedGrantRecords += maximumRecords;
        return new(budget, maximumBytes, maximumRecords);
    }

    internal void ChargeReadGrant(ReadExecutionBudgetReadGrant grant, long count)
    {
        budget.Check();
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (!grant.BelongsTo(budget) || grant.IsCompleted)
        {
            throw new ArgumentException(GrantOwnerMismatch, nameof(grant));
        }
        if (count > grant.RemainingBytes || count > reservedReadGrantBytes
            || count > budget.MaximumNativeReadBytes - budget.ReadBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ReadExecutionBudget.ReadBytesExceeded);
        }
        if (grant.RemainingRecords == EmptyElementCount || reservedGrantRecords == EmptyElementCount
            || examinedGrantRecords >= budget.MaximumNativeScanRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ExaminedRecordsExceeded);
        }
        budget.AcceptReadGrantBytes(count);
        reservedReadGrantBytes -= count;
        examinedGrantRecords++;
        reservedGrantRecords--;
        grant.Accept(count);
    }

    internal void ImportReadGrant(ReadExecutionBudgetReadGrant grant, long count, int records)
    {
        budget.Check();
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfNegative(records);
        if (!grant.BelongsTo(budget) || grant.IsCompleted)
        { throw new ArgumentException(GrantOwnerMismatch, nameof(grant)); }
        if (count > grant.RemainingBytes || count > reservedReadGrantBytes
            || count > budget.MaximumNativeReadBytes - budget.ReadBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, ReadExecutionBudget.ReadBytesExceeded); }
        if (records > grant.RemainingRecords || records > reservedGrantRecords
            || records > budget.MaximumNativeScanRecords - examinedGrantRecords)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, ExaminedRecordsExceeded); }
        budget.AcceptReadGrantBytes(count);
        reservedReadGrantBytes -= count;
        examinedGrantRecords += records;
        reservedGrantRecords -= records;
        grant.AcceptObserved(count, records);
    }
}
