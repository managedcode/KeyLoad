namespace KeyLoad.Core.Features.Authorization;

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
    { this.budget = budget; this.ownerCharge = ownerCharge; }

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
                { return null; }
                return RowVisibilityTransfers.Draft(this, budget, scope, ref acquired);
            }
            catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
            { primary = error; throw; }
            catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
            { primary = error; throw; }
            finally { acquired?.Dispose(); }
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
        { RowVisibilityFailureLedger.ThrowJoined(primary, error); throw; }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
        { RowVisibilityFailureLedger.ThrowJoined(primary, error); throw; }
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
                { found = entry; }
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
        { return true; }
        for (var entry = head; entry is not null; entry = entry.Next)
        { if (entry.Pins == RowVisibilityMemoryModel.MetadataOnly) { entry.Retired = true; } }
        Sweep();
        return budget.TryReserve(RowVisibilityMemoryModel.EntryBytes(scope), RowVisibilityMemoryModel.SingleEntry,
            out acquired);
    }

    internal bool Publish(RowVisibilityEntry entry)
    {
        lock (gate)
        {
            if (closed || Find(entry.Scope) is not null)
            { return false; }
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
            if (!chargeReleased)
            {
                if (releaseFailure is not null)
                { RowVisibilityFailureLedger.Throw([releaseFailure]); }
                try
                { charge.Dispose(); }
                catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
                { releaseFailure = error; throw; }
                catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
                { releaseFailure = error; throw; }
                chargeReleased = true;
            }
            if (!pinReleased)
            {
                entry.Pins--;
                pinReleased = true;
            }
            Sweep();
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            closed = true;
            for (var entry = head; entry is not null; entry = entry.Next)
            { entry.Retired = true; }
            Sweep();
        }
    }

    private RowVisibilityEntry? Find(RowVisibilityScope scope)
    {
        for (var entry = head; entry is not null; entry = entry.Next)
        { if (!entry.Retired && entry.Scope == scope) { return entry; } }
        return null;
    }

    private void RetireStale(RowVisibilityScope scope)
    {
        for (var entry = head; entry is not null; entry = entry.Next)
        { if (entry.Scope.SameResourcePrincipal(scope) && entry.Scope != scope) { entry.Retired = true; } }
        Sweep();
    }

    private void Sweep()
    {
        var failures = new List<Exception>();
        RowVisibilityEntry? previous = null;
        for (var entry = head; entry is not null;)
        {
            var next = entry.Next;
            var before = failures.Count;
            if (entry.Retired && entry.Pins == RowVisibilityMemoryModel.MetadataOnly)
            { RowVisibilityFailureLedger.Capture(entry.Dispose, failures); }
            if (entry.Retired && entry.Pins == RowVisibilityMemoryModel.MetadataOnly && failures.Count == before)
            {
                if (previous is null)
                { head = next; }
                else
                { previous.Next = next; }
            }
            else
            { previous = entry; }
            entry = next;
        }
        if (closed && head is null && !ownerReleased)
        {
            if (ownerReleaseFailure is not null)
            { failures.Add(ownerReleaseFailure); }
            else
            {
                RowVisibilityFailureLedger.Capture(ownerCharge.Dispose, failures);
                ownerReleaseFailure = failures.Count == RowVisibilityMemoryModel.MetadataOnly ? null : failures[^RowVisibilityMemoryModel.SingleEntry];
            }
            ownerReleased = failures.Count == RowVisibilityMemoryModel.MetadataOnly;
        }
        RowVisibilityFailureLedger.Throw(failures);
    }
}
