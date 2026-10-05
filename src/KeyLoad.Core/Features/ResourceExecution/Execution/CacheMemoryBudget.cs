using Microsoft.Extensions.Options;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Atomically admits modeled retained cache bytes and entries for one node.</summary>
public sealed class CacheMemoryBudget : ICacheMemoryBudget, IDisposable
{
    private const string InvalidBytes = "A cache reservation must charge positive modeled bytes.";
    private const string InvalidEntries = "A cache reservation cannot charge a negative entry count.";
    private const string AccountingInvariantFailed = "The cache memory reservation accounting is inconsistent.";
    private readonly Lock gate = new();
    private readonly long maxRetainedBytes;
    private readonly int maxRetainedEntries;
    private long retainedBytes;
    private int retainedEntries;
    // Positive byte charges and the 1 GiB maximum bound this count below Int32.MaxValue.
    private int activeReservations;
    private bool closed;

    /// <summary>Validates and owns immutable node admission ceilings before creating pool state.</summary>
    /// <param name="options">Centrally validated modeled retained-byte and entry ceilings.</param>
    public CacheMemoryBudget(IOptions<CacheMemoryLimits> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var limits = options.Value;
        limits.Validate();
        maxRetainedBytes = limits.MaxRetainedBytes;
        maxRetainedEntries = limits.MaxRetainedEntries;
    }

    /// <inheritdoc />
    public bool TryReserve(long bytes, int entries, out ICacheMemoryReservation? reservation)
    {
        ValidateRequest(bytes, entries);
        reservation = null;
        lock (gate)
        {
            if (closed || bytes > maxRetainedBytes - retainedBytes
                || entries > maxRetainedEntries - retainedEntries)
            {
                return false;
            }

            retainedBytes += bytes;
            retainedEntries += entries;
            activeReservations++;
        }

        try
        {
            reservation = new CacheMemoryReservation(this, bytes, entries);
            return true;
        }
        catch (Exception)
        {
            Release(bytes, entries);
            throw;
        }
    }

    /// <inheritdoc />
    public CacheMemorySnapshot GetSnapshot()
    {
        lock (gate)
        {
            return new(retainedBytes, retainedEntries, activeReservations, closed,
                maxRetainedBytes, maxRetainedEntries);
        }
    }

    /// <summary>Closes admission while preserving accounting until every lease is returned.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            closed = true;
        }
    }

    internal void Release(long bytes, int entries)
    {
        lock (gate)
        {
            if (activeReservations == 0 || retainedBytes < bytes || retainedEntries < entries)
            {
                throw new InvalidOperationException(AccountingInvariantFailed);
            }

            retainedBytes -= bytes;
            retainedEntries -= entries;
            activeReservations--;
        }
    }

    private static void ValidateRequest(long bytes, int entries)
    {
        if (bytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes), bytes, InvalidBytes);
        }

        if (entries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entries), entries, InvalidEntries);
        }
    }
}
