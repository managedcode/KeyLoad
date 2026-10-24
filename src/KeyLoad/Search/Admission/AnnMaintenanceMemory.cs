using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

/// <summary>Accounts original maintenance memory under the same gate as slots and public frames.</summary>
internal sealed class AnnMaintenanceMemory(Lock gate, List<AnnGenerationSlot> slots,
    IOptions<AnnExecutionOptions> configured, AnnReadReservations readers) : IDisposable, IAnnMaintenanceMemoryRelease
{
    private const long Empty = 0;
    private AnnMaintenanceMemoryLease? current;
    private bool closed;
    internal long Bytes
    {
        get
        {
            lock (gate)
            {
                return current?.Bytes ?? Empty;
            }
        }
    }
    internal bool Active
    {
        get
        {
            lock (gate)
            {
                return current is not null;
            }
        }
    }

    internal AnnMaintenanceMemoryLease Reserve()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (current is not null)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, AnnProtocol.Bound);
            }
            var available = Available();
            if (available <= Empty)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, AnnProtocol.Bound);
            }
            return current = new(this, available);
        }
    }

    internal void Expand(AnnMaintenanceMemoryLease lease)
    {
        lock (gate)
        {
            Require(lease);
            var available = Available();
            if (available < lease.Bytes)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, AnnProtocol.Bound);
            }
            lease.Bytes = available;
        }
    }

    internal long Remaining(long incoming)
    {
        lock (gate)
        {
            if (current is null || incoming < Empty || incoming > current.Bytes)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, AnnProtocol.Bound);
            }
            return current.Bytes - incoming;
        }
    }

    internal void Retain(AnnMaintenanceMemoryLease lease, long retained)
    {
        lock (gate)
        {
            Require(lease);
            if (retained < Empty || retained > lease.Bytes)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, AnnProtocol.Bound);
            }
            lease.Bytes = retained;
        }
    }

    internal void TransferIndex(long bytes)
    {
        lock (gate)
        {
            if (current is null)
            {
                return;
            }
            if (bytes <= Empty || bytes > current.Bytes)
            {
                throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Ownership);
            }
            current.Bytes -= bytes;
        }
    }

    public void Release(AnnMaintenanceMemoryLease lease)
    {
        lock (gate)
        {
            Require(lease);
            current = null;
        }
    }

    internal void Close() => Dispose();

    public void Dispose()
    {
        lock (gate)
        {
            closed = true;
            current?.Dispose();
            current = null;
        }
    }

    private long Available() => checked(configured.Value.MaximumResidentBytes
        - AnnRootLease.ReceiptReservation(configured.Value) - readers.Bytes
        - slots.Sum(item => item.Index.RetainedBytesUpperBound));
    private void Require(AnnMaintenanceMemoryLease lease)
    {
        if (!ReferenceEquals(current, lease))
        {
            throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Ownership);
        }
    }
}
