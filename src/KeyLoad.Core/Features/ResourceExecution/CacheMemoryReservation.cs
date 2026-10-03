namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Returns one exact modeled charge to its owning node budget at most once.</summary>
internal sealed class CacheMemoryReservation : ICacheMemoryReservation
{
    private readonly CacheMemoryBudget owner;
    private int released;

    internal CacheMemoryReservation(CacheMemoryBudget owner, long bytes, int entries)
    {
        this.owner = owner;
        Bytes = bytes;
        Entries = entries;
    }

    /// <inheritdoc />
    public long Bytes { get; }

    /// <inheritdoc />
    public int Entries { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref released, 1) == 0)
        {
            owner.Release(Bytes, Entries);
        }
    }
}
