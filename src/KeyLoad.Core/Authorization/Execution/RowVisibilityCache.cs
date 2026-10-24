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
        => RowVisibilityTransfers.CreateOwner(budget);

    internal RowVisibilityDraft? Begin(RowVisibilityScope scope)
    {
        lock (gate)
        {
            RetireStale(scope);
            return closed || Find(scope) is not null ? null : CreateDraft(scope);
        }
    }

    private RowVisibilityDraft? CreateDraft(RowVisibilityScope scope)
    {
        ICacheMemoryReservation? acquired = null;
        Exception? primary = null;
        try
        {
            try
            {
                if (!ReserveEntry(scope, out acquired))
                {
                    return null;
                }
                return RowVisibilityTransfers.Draft(this, budget, scope, ref acquired);
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
            return Pin(Find(scope));
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
            return Pin(found);
        }
    }

    private RowVisibilityLease? Pin(RowVisibilityEntry? entry)
    {
        return closed || entry is null ? null : RowVisibilityTransfers.CreateLease(this, budget, entry);
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
            if (closed || Find(entry.Scope) is not null)
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

    private RowVisibilityEntry? Find(RowVisibilityScope scope)
    {
        for (var entry = head; entry is not null; entry = entry.Next)
        {
            if (!entry.Retired && entry.Scope == scope)
            {
                return entry;
            }
        }
        return null;
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
