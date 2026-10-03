namespace KeyLoad;

/// <summary>Owns one retained-byte/entry charge until all corresponding cache state is released.</summary>
public interface ICacheMemoryReservation : IDisposable
{
    /// <summary>Positive modeled byte charge held by this reservation.</summary>
    long Bytes { get; }

    /// <summary>Nonnegative live-entry charge; an index baseline may have zero entries.</summary>
    int Entries { get; }
}

/// <summary>Shares retained cache admission across node-owned stores and cache families.</summary>
/// <remarks>Callers reserve before allocation and retain the charge across fills, pins and retirement.</remarks>
public interface ICacheMemoryBudget
{
    /// <summary>Reserves exact positive bytes and nonnegative entries, or leaves accounting unchanged.</summary>
    /// <param name="bytes">Modeled bytes including all participating owned key/value/metadata state.</param>
    /// <param name="entries">Live entries, including retired pinned entries; zero for index state.</param>
    /// <param name="reservation">Owned charge on success; null when closed or either ceiling is exceeded.</param>
    /// <returns>True only when both requested charges fit the open shared budget.</returns>
    bool TryReserve(long bytes, int entries, out ICacheMemoryReservation? reservation);

    /// <summary>Returns one consistent closed-label observation without payloads or identities.</summary>
    /// <returns>Current charges, active reservation count and configured modeled ceilings.</returns>
    CacheMemorySnapshot GetSnapshot();
}

/// <summary>Observes modeled retained cache charges; it does not report physical I/O or process RSS.</summary>
/// <param name="RetainedBytes">Current modeled bytes including still-owned retired/filling state.</param>
/// <param name="RetainedEntries">Current entry charges including pinned retired entries.</param>
/// <param name="ActiveReservations">Number of outstanding independently owned charges.</param>
/// <param name="Closed">Whether new reservations have been disabled.</param>
/// <param name="MaxRetainedBytes">Configured modeled byte ceiling.</param>
/// <param name="MaxRetainedEntries">Configured live-entry ceiling.</param>
public readonly record struct CacheMemorySnapshot(long RetainedBytes, int RetainedEntries,
    int ActiveReservations, bool Closed, long MaxRetainedBytes, int MaxRetainedEntries);
