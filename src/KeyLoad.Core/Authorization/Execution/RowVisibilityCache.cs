namespace KeyLoad.Core.Authorization;

internal sealed class RowVisibilityCache : IDisposable
{
    private readonly Lock gate = new();
    private readonly ICacheMemoryBudget budget;
    private readonly ICacheMemoryReservation ownerCharge;
    private RowVisibilityEntry? head;
    private bool closed;
    private bool ownerReleased;
    private Exception? ownerReleaseFailure;

    internal RowVisibilityCache(ICacheMemoryBudget budget, ICacheMemoryReservation ownerCharge)
    {
        this.budget = budget;
        this.ownerCharge = ownerCharge;
    }

    internal static RowVisibilityCache? Create(ICacheMemoryBudget budget)
        => (RowVisibilityTransfers.CreateOwner(budget)).Value;

    internal global::ManagedCode.Communication.Result<RowVisibilityDraft?> Begin(RowVisibilityScope scope)
    {
        lock (gate)
        {
            RetireStale(scope);
            return ((RowVisibilityDraft?)(closed || (Find(scope)).Value is not null ? null : (CreateDraft(scope)).Value)) is { } outcomeValue1 ? global::ManagedCode.Communication.Result<RowVisibilityDraft?>.Succeed(outcomeValue1) : global::ManagedCode.Communication.Result<RowVisibilityDraft?>.FailNotFound(nameof(Begin));
        }
    }

    private global::ManagedCode.Communication.Result<RowVisibilityDraft?> CreateDraft(RowVisibilityScope scope)
    {
        ICacheMemoryReservation? acquired = null;
        Exception? primary = null;
        try
        {
            try
            {
                if (!ReserveEntry(scope, out acquired))
                {
                    return global::ManagedCode.Communication.Result<RowVisibilityDraft?>.FailNotFound(nameof(CreateDraft));
                }
                return (RowVisibilityTransfers.Draft(this, budget, scope, ref acquired));
            }
            catch (Exception error)
            {
                primary = error;
                throw;
            }
            finally
            {
                acquired?.Dispose();
            }
        }
        catch (Exception error)
        {
            RowVisibilityFailureLedger.ThrowJoined(primary, error);
            throw;
        }
    }

    internal RowVisibilityLease? Acquire(RowVisibilityScope scope)
    {
        lock (gate)
        {
            RetireStale(scope);
            return (Pin((Find(scope)).Value)).Value;
        }
    }

    internal RowVisibilityLease? AcquireCurrent(RowVisibilityCurrentScope current)
    {
        lock (gate)
        {
            RowVisibilityEntry? found = null;
            for (var entry = head; entry is not null; entry = entry.Next)
            {
                if (!entry.Retired && entry.Scope.Current == current)
                {
                    found = entry;
                }
            }
            return (Pin(found)).Value;
        }
    }

    private global::ManagedCode.Communication.Result<RowVisibilityLease?> Pin(RowVisibilityEntry? entry)
    {
        return ((RowVisibilityLease?)(closed || entry is null ? null : (RowVisibilityTransfers.CreateLease(this, budget, entry)).Value)) is { } outcomeValue1 ? global::ManagedCode.Communication.Result<RowVisibilityLease?>.Succeed(outcomeValue1) : global::ManagedCode.Communication.Result<RowVisibilityLease?>.FailNotFound(nameof(Pin));
    }
    private bool ReserveEntry(RowVisibilityScope scope, out ICacheMemoryReservation? acquired)
    {
        if (budget.TryReserve(RowVisibilityMemoryModel.EntryBytes(scope), RowVisibilityMemoryModel.SingleEntry,
                out acquired))
        {
            return true;
        }
        for (var entry = head; entry is not null; entry = entry.Next)
        {
            if (entry.Pins == RowVisibilityMemoryModel.MetadataOnly)
            {
                entry.Retired = true;
            }
        }
        Sweep();
        return budget.TryReserve(RowVisibilityMemoryModel.EntryBytes(scope), RowVisibilityMemoryModel.SingleEntry,
            out acquired);
    }

    internal bool Publish(RowVisibilityEntry entry)
    {
        lock (gate)
        {
            if (closed || (Find(entry.Scope)).Value is not null)
            {
                return false;
            }
            entry.Next = head;
            head = entry;
            return true;
        }
    }

    internal bool Contains(RowVisibilityEntry entry, string id, ref bool pinReleased)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(pinReleased, this);
            return entry.Contains(id);
        }
    }

    internal void Release(RowVisibilityEntry entry, ICacheMemoryReservation charge, ref bool chargeReleased, ref bool pinReleased, ref Exception? releaseFailure)
    {
        lock (gate)
        {
            RowVisibilityRetirement.ReleasePin(entry, charge, ref chargeReleased, ref pinReleased, ref releaseFailure);
            Sweep();
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            closed = true;
            for (var entry = head; entry is not null; entry = entry.Next)
            {
                entry.Retired = true;
            }
            Sweep();
        }
    }

    private global::ManagedCode.Communication.Result<RowVisibilityEntry?> Find(RowVisibilityScope scope)
    {
        for (var entry = head; entry is not null; entry = entry.Next)
        {
            if (!entry.Retired && entry.Scope == scope)
            {
                return global::ManagedCode.Communication.Result<RowVisibilityEntry?>.Succeed(((RowVisibilityEntry?)(entry)));
            }
        }
        return global::ManagedCode.Communication.Result<RowVisibilityEntry?>.FailNotFound(nameof(Find));
    }

    private void RetireStale(RowVisibilityScope scope)
    {
        for (var entry = head; entry is not null; entry = entry.Next)
        {
            if (entry.Scope.SameResourcePrincipal(scope) && entry.Scope != scope)
            {
                entry.Retired = true;
            }
        }
        Sweep();
    }

    private void Sweep()
        => RowVisibilityRetirement.Sweep(ref head, ref closed, ownerCharge, ref ownerReleased, ref ownerReleaseFailure);
}
