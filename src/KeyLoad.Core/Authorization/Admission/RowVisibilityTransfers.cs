namespace KeyLoad.Core.Authorization;

internal static class RowVisibilityTransfers
{
    internal static (bool HasValue, RowVisibilityCache? Value) CreateOwner(ICacheMemoryBudget budget)
    {
        ICacheMemoryReservation? acquired = null;
        Exception? primary = null;
        try
        {
            try
            {
                if (!budget.TryReserve(RowVisibilityMemoryModel.OwnerBytes, RowVisibilityMemoryModel.MetadataOnly, out acquired))
                {
                    return (false, default(RowVisibilityCache?));
                }
                return (true, (Owner(budget, ref acquired)));
            }
            catch (Exception error)
            {
                primary = error;
                throw;
            }
            finally { acquired?.Dispose(); }
        }
        catch (Exception error)
        {
            RowVisibilityFailureLedger.ThrowJoined(primary, error);
            throw;
        }
    }
    private static RowVisibilityCache Owner(ICacheMemoryBudget budget, ref ICacheMemoryReservation? acquired)
    {
        var cache = new RowVisibilityCache(budget, acquired!);
        acquired = null;
        return cache;
    }

    internal static RowVisibilityDraft Draft(RowVisibilityCache owner, ICacheMemoryBudget budget,
        RowVisibilityScope scope, ref ICacheMemoryReservation? acquired)
    {
        var draft = new RowVisibilityDraft(owner, budget, scope, acquired!);
        acquired = null;
        return draft;
    }

    internal static RowVisibilityLease Lease(RowVisibilityCache owner, RowVisibilityEntry entry,
        ref ICacheMemoryReservation? acquired)
    {
        var lease = new RowVisibilityLease(owner, entry, acquired!);
        entry.Pins++;
        acquired = null;
        return lease;
    }

    internal static (bool HasValue, RowVisibilityLease? Value) CreateLease(RowVisibilityCache owner,
        ICacheMemoryBudget budget, RowVisibilityEntry entry)
    {
        ICacheMemoryReservation? acquired = null;
        Exception? primary = null;
        try
        {
            try
            {
                if (!budget.TryReserve(RowVisibilityMemoryModel.LeaseBytes, RowVisibilityMemoryModel.MetadataOnly, out acquired))
                {
                    return (false, default(RowVisibilityLease?));
                }
                return (true, (Lease(owner, entry, ref acquired)));
            }
            catch (Exception error)
            {
                primary = error;
                throw;
            }
            finally { acquired?.Dispose(); }
        }
        catch (Exception error)
        {
            RowVisibilityFailureLedger.ThrowJoined(primary, error);
            throw;
        }
    }

}
