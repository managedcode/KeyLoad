namespace KeyLoad.Core.Features.Authorization;

internal sealed class RowVisibilityEntry(
    RowVisibilityScope scope, ICacheMemoryBudget budget, ICacheMemoryReservation charge) : IDisposable
{
    private IdNode? ids;
    private readonly SortedSet<string> index = new(StringComparer.Ordinal);
    private bool disposed;
    private Exception? releaseFailure;
    internal RowVisibilityScope Scope { get; } = scope;
    internal RowVisibilityEntry? Next
    {
        get; set;
    }
    internal int Pins
    {
        get; set;
    }
    internal bool Retired
    {
        get; set;
    }

    internal bool Contains(string id)
    {
        return index.Contains(id);
    }

    internal bool Add(string id)
    {
        ICacheMemoryReservation? acquired = null;
        Exception? primary = null;
        try
        {
            try
            {
                if (!budget.TryReserve(RowVisibilityMemoryModel.IdBytes(id), RowVisibilityMemoryModel.SingleEntry, out acquired))
                {
                    return false;
                }
                ids = new(id, acquired!, ids);
                acquired = null;
                index.Add(id);
            }
            catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
            {
                primary = error;
                throw;
            }
            catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
            {
                primary = error;
                throw;
            }
            finally { acquired?.Dispose(); }
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
        {
            RowVisibilityFailureLedger.ThrowJoined(primary, error);
            throw;
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
        {
            RowVisibilityFailureLedger.ThrowJoined(primary, error);
            throw;
        }
        return true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        if (releaseFailure is not null)
        {
            RowVisibilityFailureLedger.Throw([releaseFailure]);
        }
        index.Clear();
        var failures = new List<Exception>();
        IdNode? previous = null;
        for (var node = ids; node is not null;)
        {
            var next = node.Next;
            var before = failures.Count;
            RowVisibilityFailureLedger.Capture(node.Charge.Dispose, failures);
            if (failures.Count == before)
            {
                if (previous is null)
                {
                    ids = next;
                }
                else
                {
                    previous.Next = next;
                }
            }
            else
            {
                previous = node;
            }
            node = next;
        }
        if (ids is null)
        {
            RowVisibilityFailureLedger.Capture(charge.Dispose, failures);
            disposed = failures.Count == RowVisibilityMemoryModel.MetadataOnly;
        }
        if (failures.Count > RowVisibilityMemoryModel.MetadataOnly)
        {
            releaseFailure = failures.Count == RowVisibilityMemoryModel.SingleEntry ? failures[RowVisibilityMemoryModel.MetadataOnly] : new AggregateException(failures);
        }
        RowVisibilityFailureLedger.Throw(failures);
    }

    private sealed class IdNode(string id, ICacheMemoryReservation charge, IdNode? next)
    {
        internal string Id { get; } = id;
        internal ICacheMemoryReservation Charge { get; } = charge;
        internal IdNode? Next { get; set; } = next;
    }
}
