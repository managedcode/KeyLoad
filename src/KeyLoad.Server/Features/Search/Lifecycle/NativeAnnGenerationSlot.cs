using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeAnnGenerationSlot(NativeAnnGenerationOwner owner, NativeAnnManifest manifest,
    PackedAnnIndex index)
{
    private const int Empty = 0;
    private const int Step = 1;
    internal NativeAnnManifest Manifest { get; } = manifest;
    internal PackedAnnIndex Index { get; } = index;
    internal int Readers { get; private set; }
    internal bool Retired { get; private set; }

    internal NativeAnnIndexLease Acquire(int maximumReaders)
    {
        if (Retired)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, NativeAnnProtocol.Stale); }
        if (Readers == maximumReaders)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, NativeAnnProtocol.Bound); }
        var lease = new NativeAnnIndexLease(this);
        Readers += Step;
        return lease;
    }

    internal void Retire() => Retired = true;
    internal void Release() => owner.Release(this);

    internal void ReleaseUnderOwnerGate()
    {
        if (Readers <= Empty)
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Ownership); }
        Readers -= Step;
    }
}
