namespace KeyLoad.Server.Features.BlobStorage;

internal sealed class NativeBlobWireCallbackLifetime
{
    private const int NoActiveCallbacks = 0;
    private readonly Lock gate = new();
    private readonly TaskCompletionSource joined = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool closed;
    private int active;

    internal void Enter()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            active++;
        }
    }

    internal void Leave()
    {
        lock (gate)
        {
            if (active <= NoActiveCallbacks)
            { throw new InvalidOperationException(NativeBlobWireProbeProtocol.InvalidEvidence); }
            active--;
            if (closed && active == NoActiveCallbacks)
            { joined.TrySetResult(); }
        }
    }

    internal Task CloseAsync()
    {
        lock (gate)
        {
            closed = true;
            if (active == NoActiveCallbacks)
            { joined.TrySetResult(); }
            return joined.Task;
        }
    }
}
