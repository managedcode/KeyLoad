using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution.Execution;

/// <summary>Executes gate-scoped reads through the same original budget and execution observers.</summary>
internal static class ReadExecutionBudgetStorageReads
{
    private static StorageReadObserver? Observer(ReadExecutionBudget budget, IKeyValueView view)
        => view is ResourceEffectPreparationTransaction prepared && ReferenceEquals(prepared.Work, budget)
            ? null : budget.ChargeReadRecord;

    internal static byte[]? Read(ReadExecutionBudget budget, IKeyValueView view, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(view);
        budget.Check();
        byte[]? value = null;
        view.ReadValue(key, borrowed => value = borrowed.ToArray(), Observer(budget, view));
        budget.Check();
        return value;
    }

    internal static T? ReadRecord<T>(ReadExecutionBudget budget, IKeyValueView view, byte[] key) where T : class
    {
        ArgumentNullException.ThrowIfNull(view);
        budget.Check();
        T? record = null;
        view.ReadValue(key, value => record = OrleansSerialization.Deserialize<T>(value), Observer(budget, view));
        budget.Check();
        return record;
    }

    internal static StorageScanResult VisitRange(ReadExecutionBudget budget, IKeyValueView view, byte[] prefix,
        int maxRecords, StorageRecordVisitor visitor, byte[]? afterKey, byte[]? untilKey)
    {
        ArgumentNullException.ThrowIfNull(view);
        budget.Check();
        var result = view.VisitRange(prefix, maxRecords, visitor, afterKey, untilKey,
            Observer(budget, view), budget.Cancellation);
        budget.Check();
        return result;
    }

    internal static ScanPage Scan(ReadExecutionBudget budget, IKeyValueView view, byte[] prefix,
        int maxRecords, byte[]? afterKey)
    {
        var records = new List<KeyValueRecord>();
        var result = VisitRange(budget, view, prefix, maxRecords, (key, value) =>
        {
            records.Add(new(key.ToArray(), value.ToArray()));
            return true;
        }, afterKey, untilKey: null);
        return new([.. records], result.HasMore);
    }
}
