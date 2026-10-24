using KeyLoad.Query.Search;

namespace KeyLoad.Server.Search;

internal sealed class AnnIndexLease : IDisposable
{
    private AnnGenerationSlot? slot;
    internal AnnIndexLease(AnnGenerationSlot slot) => this.slot = slot;
    internal PackedAnnIndex Index => Current.Index;
    internal AnnManifest Manifest => Current.Manifest;
    private AnnGenerationSlot Current => slot ?? throw new ObjectDisposedException(nameof(AnnIndexLease));
    public void Dispose() => Interlocked.Exchange(ref slot, null)?.Release();
}
