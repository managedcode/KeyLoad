using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Charges one read operation before borrowed storage data is copied or consumed.</summary>
internal sealed class BudgetedReadView : IKeyValueView
{
    private readonly IKeyValueView view;
    private readonly ReadExecutionBudget budget;

    /// <summary>Creates a view whose lifetime is bounded by the wrapped storage action.</summary>
    /// <param name="view">The underlying view, valid only in its current storage action.</param>
    /// <param name="budget">The shared operation budget.</param>
    public BudgetedReadView(IKeyValueView view, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(budget);
        this.view = view;
        this.budget = budget;
    }

    /// <inheritdoc />
    public byte[]? ReadOwnedValue(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        byte[]? result = null;
        ReadValue(key, value => result = value.ToArray());
        return result;
    }

    /// <inheritdoc />
    public ScanPage Scan(byte[] prefix, int maxRecords, byte[]? afterKey = null)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRecords);
        var records = new List<KeyValueRecord>();
        var result = VisitRange(prefix, maxRecords, (key, value) =>
        {
            records.Add(new(key.ToArray(), value.ToArray()));
            return true;
        }, afterKey);
        return new([.. records], result.HasMore);
    }

    /// <inheritdoc />
    public bool ReadValue(byte[] key, StorageValueReader reader, StorageReadObserver? observer = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(reader);
        budget.Check();
        var found = view.ReadValue(key, reader, bytes =>
        {
            budget.ChargeBytes(bytes);
            observer?.Invoke(bytes);
            budget.Check();
        });
        budget.Check();
        return found;
    }

    /// <inheritdoc />
    public StorageScanResult VisitRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default)
        => VisitRangeWithDirection(prefix, maxRecords, visitor, afterKey, untilKey, observer, false, cancellationToken);

    /// <inheritdoc />
    public StorageScanResult VisitReverseRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default)
        => VisitRangeWithDirection(prefix, maxRecords, visitor, afterKey, untilKey, observer, true, cancellationToken);

    private StorageScanResult VisitRangeWithDirection(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey, byte[]? untilKey, StorageReadObserver? observer, bool reverse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(visitor);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRecords);
        budget.Check();
        using var linked = CreateLinkedToken(cancellationToken, out var effectiveToken);
        var result = VisitRangeCore(prefix, maxRecords, visitor, afterKey, untilKey, observer, reverse, effectiveToken);
        effectiveToken.ThrowIfCancellationRequested();
        budget.Check();
        return result;
    }

    private StorageScanResult VisitRangeCore(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey, byte[]? untilKey, StorageReadObserver? observer, bool reverse, CancellationToken cancellationToken)
    {
        StorageReadObserver charge = bytes =>
        {
            budget.ChargeBytes(bytes);
            observer?.Invoke(bytes);
            cancellationToken.ThrowIfCancellationRequested();
            budget.Check();
        };
        return reverse ? view.VisitReverseRange(prefix, maxRecords, visitor, afterKey, untilKey, charge, cancellationToken)
            : view.VisitRange(prefix, maxRecords, visitor, afterKey, untilKey, charge, cancellationToken);
    }

    private CancellationTokenSource? CreateLinkedToken(CancellationToken viewToken, out CancellationToken effectiveToken)
    {
        var budgetToken = budget.Cancellation;
        if (!viewToken.CanBeCanceled || viewToken == budgetToken)
        {
            effectiveToken = budgetToken;
            return null;
        }
        if (!budgetToken.CanBeCanceled)
        {
            effectiveToken = viewToken;
            return null;
        }
        var linked = CancellationTokenSource.CreateLinkedTokenSource(budgetToken, viewToken);
        effectiveToken = linked.Token;
        return linked;
    }
}
