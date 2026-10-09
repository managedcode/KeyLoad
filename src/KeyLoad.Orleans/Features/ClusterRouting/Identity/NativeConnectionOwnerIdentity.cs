namespace KeyLoad.Orleans;

/// <summary>Disposable identity of one silo service execution owner.</summary>
public sealed class NativeConnectionOwnerIdentity
{
    /// <summary>Gets the stable identity created once for this registered service lifetime.</summary>
    public Guid Id { get; } = Guid.NewGuid();
}
