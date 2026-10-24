namespace KeyLoad.Server.Search;

internal sealed class TextOnlineGenerationPin(TextOnlineGeneration owner) : IDisposable
{
    private readonly Lock gate = new();
    private bool disposed;
    internal TextOnlineGeneration Owner => owner;

    internal void RequireActive()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            owner.RequireActive();
        }
    }

    internal void RequirePath(string generationPath)
    {
        RequireActive();
        if (!StringComparer.Ordinal.Equals(generationPath, Path.Combine(owner.Root, owner.Original.Authority.Leaf)))
        {
            throw TextErrors.Ownership();
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            owner.ReleasePin();
            disposed = true;
        }
    }
}
