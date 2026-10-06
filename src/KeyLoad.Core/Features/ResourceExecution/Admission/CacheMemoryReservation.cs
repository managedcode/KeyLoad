namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Returns one exact modeled charge to its owning node budget at most once.</summary>
internal sealed class CacheMemoryReservation : ICacheMemoryReservation
{
    private const int SingleElementCount = 1;
    private const int EmptyElementCount = 0;

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
        if (Interlocked.Exchange(ref released, SingleElementCount) == EmptyElementCount)
        {
            owner.Release(Bytes, Entries);
        }
    }
}
