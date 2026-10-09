namespace KeyLoad.Orleans;

/// <summary>Releases one original connection operation only after its stream has settled.</summary>
internal sealed class NativeConnectionOperationLease(NativeConnectionOperationOwner owner, Guid requestId) : IDisposable
{
    private bool disposed;

    internal CancellationToken ShutdownToken => owner.ShutdownToken;

    public void Dispose()
    {
        if (disposed)
        { return; }
        disposed = true;
        owner.Release(requestId);
    }
}
