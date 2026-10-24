using KeyLoad.Query.Search;

namespace KeyLoad.Server.Search;

internal sealed class AnnGenerationSlot(AnnGenerationOwner owner, AnnManifest manifest,
    PackedAnnIndex index)
{
    private const int Empty = 0;
    private const int Step = 1;
    internal AnnManifest Manifest { get; } = manifest;
    internal PackedAnnIndex Index { get; } = index;
    internal int Readers
    {
        get; private set;
    }
    internal bool Retired
    {
        get; private set;
    }

    internal AnnIndexLease Acquire(int maximumReaders)
    {
        if (Retired)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, AnnProtocol.Stale);
        }
        if (Readers == maximumReaders)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, AnnProtocol.Bound);
        }
        var lease = new AnnIndexLease(this);
        Readers += Step;
        return lease;
    }

    internal void Retire() => Retired = true;
    internal void Release() => owner.Release(this);

    internal void ReleaseUnderOwnerGate()
    {
        if (Readers <= Empty)
        {
            throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Ownership);
        }
        Readers -= Step;
    }
}
