using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution.Execution;

/// <summary>Owns scoped read-grant reservations on the same operation ledger, lifetime and cancellation state.</summary>
internal sealed class ReadExecutionBudgetScopedReadGrants(ReadExecutionBudget budget)
{
    private const string ExaminedRecordsExceeded = "The read execution examined-record budget is exceeded.";

    private const string GrantOwnerMismatch = "The read grant belongs to a different operation.";

    private long reservedReadGrantBytes;

    private int examinedGrantRecords;

    private int reservedGrantRecords;

    private ReadExecutionBudgetReadGrant? scopedReadGrant;

    internal long ReservedBytes => reservedReadGrantBytes;
    internal ReadExecutionBudgetReadGrant? Current => scopedReadGrant;

    internal int ClaimedRecords => examinedGrantRecords + reservedGrantRecords;
    internal int ExaminedRecords => examinedGrantRecords;

    internal void CompleteReadGrant(ReadExecutionBudgetReadGrant grant)
    {
        budget.Check();
        ArgumentNullException.ThrowIfNull(grant);
        if (!grant.BelongsTo(budget) || grant.IsCompleted)
        {
            throw new ArgumentException(GrantOwnerMismatch, nameof(grant));
        }
        reservedReadGrantBytes -= grant.RemainingBytes;
        reservedGrantRecords -= grant.RemainingRecords;
        grant.Complete();
    }

    internal void CompleteSettledReadGrant(ReadExecutionBudgetReadGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        if (!grant.BelongsTo(budget) || grant.IsCompleted || ReferenceEquals(scopedReadGrant, grant))
        {
            throw new ArgumentException(GrantOwnerMismatch, nameof(grant));
        }
        reservedReadGrantBytes -= grant.RemainingBytes;
        reservedGrantRecords -= grant.RemainingRecords;
        grant.Complete();
    }

    internal ReadExecutionBudgetReadGrantLease EnterReadGrant(ReadExecutionBudgetReadGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        budget.Check();
        if (!grant.BelongsTo(budget) || grant.IsCompleted || scopedReadGrant is not null)
        {
            throw new ArgumentException(GrantOwnerMismatch, nameof(grant));
        }
        scopedReadGrant = grant;
        return new(budget, grant);
    }

    internal void ExitReadGrant(ReadExecutionBudgetReadGrant grant)
    {
        if (!ReferenceEquals(scopedReadGrant, grant))
        {
            throw new ArgumentException(GrantOwnerMismatch, nameof(grant));
        }
        scopedReadGrant = null;
    }

    internal void ChargeReadRecord(long count)
    {
        if (scopedReadGrant is { } grant)
        {
            ChargeReadGrant(grant, count);
        }
        else
        {
            budget.ChargeUnscopedRecord(count);
        }
    }

    internal IKeyValueView CreateView(IKeyValueView view, ReadExecutionBudgetReadGrant grant)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(grant);
        budget.Check();
        if (!grant.BelongsTo(budget) || grant.IsCompleted
            || scopedReadGrant is not null && !ReferenceEquals(scopedReadGrant, grant))
        {
            throw new ArgumentException(GrantOwnerMismatch, nameof(grant));
        }
        return new BudgetedReadView(view, budget, grant);
    }

    /// <summary>Reserves one non-borrowable raw-byte ceiling for a sequential query leaf.</summary>
    /// <param name="maximumBytes">Maximum accepted execution bytes reserved for that leaf.</param>
    /// <param name="maximumRecords">Maximum execution point attempts and range records reserved for that leaf.</param>
    /// <returns>A reader sharing this operation's aggregate bytes, deadline and cancellation.</returns>
    internal ReadExecutionBudgetReadGrant CreateReadGrant(long maximumBytes, int maximumRecords)
    {
        budget.Check();
        ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumRecords);
        if (maximumBytes > budget.RuntimeMaximumReadBytes - budget.ReadBytes - reservedReadGrantBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ReadExecutionBudget.ReadBytesExceeded);
        }
        if (maximumRecords > budget.MaximumScanRecords - examinedGrantRecords - reservedGrantRecords)
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
            || count > budget.RuntimeMaximumReadBytes - budget.ReadBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ReadExecutionBudget.ReadBytesExceeded);
        }
        if (grant.RemainingRecords == 0 || reservedGrantRecords == 0
            || examinedGrantRecords >= budget.MaximumScanRecords)
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
        ImportSettledReadGrant(grant, count, records);
    }

    internal void ImportSettledReadGrant(ReadExecutionBudgetReadGrant grant, long count, int records)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfNegative(records);
        if (!grant.BelongsTo(budget) || grant.IsCompleted)
        {
            throw new ArgumentException(GrantOwnerMismatch, nameof(grant));
        }
        if (count > grant.RemainingBytes || count > reservedReadGrantBytes
            || count > budget.RuntimeMaximumReadBytes - budget.ReadBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ReadExecutionBudget.ReadBytesExceeded);
        }
        if (records > grant.RemainingRecords || records > reservedGrantRecords
            || records > budget.MaximumScanRecords - examinedGrantRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ExaminedRecordsExceeded);
        }
        budget.AcceptReadGrantBytes(count);
        reservedReadGrantBytes -= count;
        examinedGrantRecords += records;
        reservedGrantRecords -= records;
        grant.AcceptObserved(count, records);
    }
}
