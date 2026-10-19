namespace KeyLoad.Clustering;

/// <summary>Disposable identity of one silo service execution owner.</summary>
public sealed class ConnectionOwnerIdentity
{
    /// <summary>Gets the stable identity created once for this registered service lifetime.</summary>
    public Guid Id { get; } = Guid.NewGuid();
}
