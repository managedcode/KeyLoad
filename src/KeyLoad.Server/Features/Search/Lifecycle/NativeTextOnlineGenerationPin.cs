namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextOnlineGenerationPin(NativeTextOnlineGeneration owner) : IDisposable
{
    private readonly Lock gate = new();
    private bool disposed;
    internal NativeTextOnlineGeneration Owner => owner;

    internal void RequireActive()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            owner.RequireActive();
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            { return; }
            owner.ReleasePin();
            disposed = true;
        }
    }
}
