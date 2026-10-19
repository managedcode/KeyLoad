namespace KeyLoad.Clustering;

/// <summary>Releases one original connection operation only after its stream has settled.</summary>
internal sealed class ConnectionOperationLease(ConnectionOperationOwner owner, Guid requestId) : IDisposable
{
    private bool disposed;

    internal CancellationToken ShutdownToken => owner.ShutdownToken;

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        owner.Release(requestId);
    }
}
