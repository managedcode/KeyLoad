namespace KeyLoad.Orleans;

/// <summary>Owns one exact admission in the silo-local request work registry.</summary>
internal sealed class NativeRequestWorkLease : IDisposable
{
    private NativeRequestWorkOwner? owner;

    internal NativeRequestWorkLease(NativeRequestWorkOwner owner, Guid requestId, NativeRequestWorkKind kind)
    {
        this.owner = owner;
        RequestId = requestId;
        Kind = kind;
    }

    internal Guid RequestId { get; }

    internal NativeRequestWorkKind Kind { get; }

    public void Dispose() => Interlocked.Exchange(ref owner, null)?.Release(this);
}
