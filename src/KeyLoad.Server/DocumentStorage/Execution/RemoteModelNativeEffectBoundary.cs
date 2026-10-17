namespace KeyLoad.Server.Features.DocumentStorage;

internal sealed class RemoteModelNativeEffectBoundary
{
    private const int NotStarted = 0;
    private const int Started = 1;
    private int started;
    internal bool IsStarted => Volatile.Read(ref started) != NotStarted;

    internal void Begin(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Interlocked.Exchange(ref started, Started);
    }
}
