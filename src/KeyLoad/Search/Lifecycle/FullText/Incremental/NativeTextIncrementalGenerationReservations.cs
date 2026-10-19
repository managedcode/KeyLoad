namespace KeyLoad.Server.Features.Search;

/// <summary>Tracks retained native generation reservations on the original incremental worker.</summary>
internal sealed class NativeTextIncrementalGenerationReservations
{
    private readonly Dictionary<string, NativeTextResourceReservation> generationReservations = new(StringComparer.Ordinal);

    internal void RetainGeneration(string leaf, NativeTextResourceReservation original)
    {
        // The worker owns this registry; every entry corresponds to prospective or actual retained files.
        if (!generationReservations.TryAdd(leaf, original))
        {
            throw NativeTextErrors.Ownership();
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
