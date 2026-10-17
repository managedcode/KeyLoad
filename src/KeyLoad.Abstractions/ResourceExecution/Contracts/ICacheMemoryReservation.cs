namespace KeyLoad;

/// <summary>Owns one retained-byte/entry charge until all corresponding cache state is released.</summary>
public interface ICacheMemoryReservation : IDisposable
{
    /// <summary>Positive modeled byte charge held by this reservation.</summary>
    long Bytes
    {
        get;
    }

    /// <summary>Nonnegative live-entry charge; an index baseline may have zero entries.</summary>
    int Entries
    {
        get;
    }
}
