namespace KeyLoad.Server.Search;

/// <summary>Tracks retained execution generation reservations on the original incremental worker.</summary>
internal sealed class TextIncrementalGenerationReservations
{
    private readonly Dictionary<string, TextResourceReservation> generationReservations = new(StringComparer.Ordinal);

    internal void RetainGeneration(string leaf, TextResourceReservation original)
    {
        // The worker owns this registry; every entry corresponds to prospective or actual retained files.
        if (!generationReservations.TryAdd(leaf, original))
        {
            throw TextErrors.Ownership();
        }
    }

    internal void ReleaseGeneration(string leaf)
    {
        if (generationReservations.TryGetValue(leaf, out var original))
        {
            original.CompleteAfterJoinedCleanup();
            generationReservations.Remove(leaf);
        }
    }

}
