using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution.Execution;

/// <summary>Enforces one fixed leaf ceiling while charging the shared operation ledger.</summary>
internal sealed class ReadExecutionBudgetReadGrant
{
    private readonly ReadExecutionBudget budget;
    private readonly long maximumBytes;
    private readonly int maximumRecords;
    private long acceptedBytes;
    private int examinedRecords;
    private bool completed;

    internal ReadExecutionBudgetReadGrant(ReadExecutionBudget budget, long maximumBytes, int maximumRecords)
    {
        ArgumentNullException.ThrowIfNull(budget);
        this.budget = budget;
        this.maximumBytes = maximumBytes;
        this.maximumRecords = maximumRecords;
    }

    internal bool IsCompleted => completed;

    internal void Complete() => completed = true;

    internal long ReadBytes => acceptedBytes;

    internal int ExaminedRecords => examinedRecords;

    internal long RemainingBytes => maximumBytes - acceptedBytes;

    internal int RemainingRecords => maximumRecords - examinedRecords;

    internal bool ReadValue(IKeyValueView view, byte[] key, StorageValueReader reader)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(reader);
        budget.Check();
        var found = view.ReadValue(key, reader, ChargeBytes);
        budget.Check();
        return found;
    }

    internal StorageScanResult VisitRange(IKeyValueView view, byte[] prefix, int maxRecords,
        StorageRecordVisitor visitor, byte[]? afterKey = null, byte[]? untilKey = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(visitor);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRecords);
        budget.Check();
        var result = view.VisitRange(prefix, maxRecords, visitor, afterKey, untilKey, ChargeBytes, budget.Cancellation);
        budget.Check();
        return result;
    }

    internal bool BelongsTo(ReadExecutionBudget owner) => ReferenceEquals(budget, owner);

    internal void Accept(long count)
    {
        acceptedBytes += count;
        examinedRecords++;
    }

    internal void AcceptObserved(long count, int records)
    {
        acceptedBytes += count;
        examinedRecords += records;
    }

    private void ChargeBytes(long count) => budget.ChargeReadGrant(this, count);
}
