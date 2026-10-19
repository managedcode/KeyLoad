using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeAnnIndexLease : IDisposable
{
    private NativeAnnGenerationSlot? slot;
    internal NativeAnnIndexLease(NativeAnnGenerationSlot slot) => this.slot = slot;
    internal PackedAnnIndex Index => Current.Index;
    internal NativeAnnManifest Manifest => Current.Manifest;
    private NativeAnnGenerationSlot Current => slot ?? throw new ObjectDisposedException(nameof(NativeAnnIndexLease));
    public void Dispose() => Interlocked.Exchange(ref slot, null)?.Release();
}
