namespace KeyLoad;

/// <summary>Observes modeled retained cache charges; it does not report physical I/O or process RSS.</summary>
/// <param name="RetainedBytes">Current modeled bytes including still-owned retired/filling state.</param>
/// <param name="RetainedEntries">Current entry charges including pinned retired entries.</param>
/// <param name="ActiveReservations">Number of outstanding independently owned charges.</param>
/// <param name="Closed">Whether new reservations have been disabled.</param>
/// <param name="MaxRetainedBytes">Configured modeled byte ceiling.</param>
/// <param name="MaxRetainedEntries">Configured live-entry ceiling.</param>
public readonly record struct CacheMemorySnapshot(long RetainedBytes, int RetainedEntries,
    int ActiveReservations, bool Closed, long MaxRetainedBytes, int MaxRetainedEntries);
