namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextResourceReservation(NativeTextResourceOwnership owner, string? generation)
{
    private readonly Lock gate = new();
    private bool completed;

    internal void RequireGeneration(NativeTextResourceOwnership expectedOwner, string path)
    {
        lock (gate)
        {
            if (completed || !ReferenceEquals(owner, expectedOwner) || generation != Path.GetFullPath(path))
            { throw NativeTextErrors.Ownership(); }
        }
    }

    // The caller invokes this only after its actual native reader/owner cleanup has joined.
    internal void CompleteAfterJoinedCleanup()
    {
        lock (gate)
        {
            if (completed)
            { throw NativeTextErrors.Ownership(); }
            owner.Release(generation);
            completed = true;
        }
    }
}
