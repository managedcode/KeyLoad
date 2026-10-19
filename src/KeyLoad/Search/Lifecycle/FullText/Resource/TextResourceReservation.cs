namespace KeyLoad.Server.Features.Search;

internal sealed class TextResourceReservation(TextResourceOwnership owner, string? generation)
{
    private readonly Lock gate = new();
    private bool completed;

    internal void RequireGeneration(TextResourceOwnership expectedOwner, string path)
    {
        lock (gate)
        {
            if (completed || !ReferenceEquals(owner, expectedOwner) || generation != Path.GetFullPath(path))
            {
                throw TextErrors.Ownership();
            }
        }
    }

    // The caller invokes this only after its actual execution reader/owner cleanup has joined.
    internal void CompleteAfterJoinedCleanup()
    {
        lock (gate)
        {
            if (completed)
            {
                throw TextErrors.Ownership();
            }
            owner.Release(generation);
            completed = true;
        }
    }
}
