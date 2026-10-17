namespace KeyLoad;

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
